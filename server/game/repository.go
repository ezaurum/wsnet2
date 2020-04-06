package game

import (
	"context"
	crand "crypto/rand"
	"encoding/hex"
	"fmt"
	"math"
	"math/big"
	"math/rand"
	"reflect"
	"strings"
	"sync"
	"time"

	"github.com/jmoiron/sqlx"
	"golang.org/x/xerrors"

	"wsnet2/config"
	"wsnet2/log"
	"wsnet2/pb"
)

const (
	// RoomID文字列長
	lenId = 16
)

var (
	roomInsertQuery string
	roomUpdateQuery string
)

func init() {
	seed, _ := crand.Int(crand.Reader, big.NewInt(math.MaxInt64))
	rand.Seed(seed.Int64())

	initQueries()
}

func initQueries() {
	// room_info queries
	t := reflect.TypeOf(pb.RoomInfo{})
	cols := make([]string, 0, t.NumField())
	for i := 0; i < t.NumField(); i++ {
		if c := t.Field(i).Tag.Get("db"); c != "" {
			cols = append(cols, c)
		}
	}
	roomInsertQuery = fmt.Sprintf("INSERT INTO room (%s) VALUES (:%s)",
		strings.Join(cols, ","), strings.Join(cols, ",:"))

	var sets []string
	for _, c := range cols {
		if c != "id" {
			sets = append(sets, c+"=:"+c)
		}
	}
	roomUpdateQuery = fmt.Sprintf("UPDATE room SET %s WHERE id=:id", strings.Join(sets, ","))
}

func RandomHex(n int) string {
	b := make([]byte, n)
	_, _ = rand.Read(b) // rand.Read always success.
	return hex.EncodeToString(b)
}

type Repository struct {
	hostId uint32

	app  pb.App
	conf *config.GameConf
	db   *sqlx.DB

	mu    sync.Mutex
	rooms map[RoomID]*Room
}

func NewRepos(db *sqlx.DB, conf *config.GameConf) (map[pb.AppId]*Repository, error) {
	hostId := uint32(1) // TODO: ちゃんとした値を取得

	query := "SELECT id, `key` FROM app"
	var apps []pb.App
	err := db.Select(&apps, query)
	if err != nil {
		return nil, xerrors.Errorf("select apps error: %w", err)
	}
	repos := make(map[pb.AppId]*Repository, len(apps))
	for _, app := range apps {
		log.Debugf("new repository: app=%#v", app.Id)
		repos[app.Id] = &Repository{
			hostId: hostId,
			app:    app,
			conf:   conf,
			db:     db,

			rooms: make(map[RoomID]*Room),
		}
	}
	return repos, nil
}

func (repo *Repository) CreateRoom(ctx context.Context, op *pb.RoomOption, master *pb.ClientInfo) (*pb.RoomInfo, error) {
	tx, err := repo.db.Beginx()
	if err != nil {
		return nil, xerrors.Errorf("begin error: %w", err)
	}

	info, err := repo.newRoomInfo(ctx, tx, op)
	if err != nil {
		tx.Rollback()
		return nil, xerrors.Errorf("", err)
	}

	room := NewRoom(info.Clone(), master.Clone())

	repo.mu.Lock()
	defer repo.mu.Unlock()
	repo.rooms[room.ID()] = room
	tx.Commit()

	return info, nil
}

func (repo *Repository) newRoomInfo(ctx context.Context, tx *sqlx.Tx, op *pb.RoomOption) (*pb.RoomInfo, error) {
	ri := &pb.RoomInfo{
		AppId:          repo.app.Id,
		HostId:         repo.hostId,
		Visible:        op.Visible,
		Watchable:      op.Watchable,
		SearchGroup:    op.SearchGroup,
		ClientDeadline: op.ClientDeadline,
		MaxPlayers:     op.MaxPlayers,
		Players:        1,
		PublicProps:    op.PublicProps,
		PrivateProps:   op.PrivateProps,
	}
	ri.SetCreated(time.Now())

	maxNumber := int32(repo.conf.MaxRoomNum)
	retryCount := repo.conf.RetryCount
	var err error
	for n := 0; n < retryCount; n++ {
		select {
		case <-ctx.Done():
			return nil, xerrors.Errorf("ctx done: %w", ctx.Err())
		default:
		}

		ri.Id = RandomHex(lenId)
		if op.WithNumber {
			ri.Number = rand.Int31n(maxNumber) + 1 // [1..maxNumber]
		}

		_, err = tx.NamedExecContext(ctx, roomInsertQuery, ri)
		if err == nil {
			return ri, nil
		}
	}

	return nil, xerrors.Errorf("NewRoomInfo try %d times: %w", retryCount, err)
}
