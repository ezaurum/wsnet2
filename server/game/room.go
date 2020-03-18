package game

import (
	"fmt"
	"sync"
	"time"

	"wsnet2/log"
	"wsnet2/pb"
)

const (
	// RoomMsgChSize : Msgチャネルのバッファサイズ
	RoomMsgChSize = 10

	// RoomDefaultClientDeadline : クライアント切断判定の無通信時間の初期値
	RoomDefaultClientDeadline = 30 * time.Second
)

type RoomID string

type Room struct {
	*pb.RoomInfo

	deadline time.Duration

	msgCh    chan Msg
	done     chan struct{}
	wgClient sync.WaitGroup

	muClients sync.RWMutex
	clients   map[ClientID]*Client
	leaved    map[ClientID]error
	master    *Client
	order     []ClientID

	logger log.Logger
}

func NewRoom(info *pb.RoomInfo, masterInfo *pb.ClientInfo) *Room {
	r := &Room{
		RoomInfo: info,

		msgCh: make(chan Msg, RoomMsgChSize),
		done:  make(chan struct{}),

		clients: make(map[ClientID]*Client),
		leaved:  make(map[ClientID]error),
		order:   []ClientID{},

		logger: log.Get(log.CurrentLevel()),
	}

	r.wgClient.Add(1)
	master := NewClient(masterInfo, r)
	r.master = master
	r.clients[ClientID(master.Id)] = master
	r.order = append(r.order, master.ID())

	go r.MsgLoop()
	r.Post(MsgCreate{})

	return r
}

func (r *Room) ID() RoomID {
	return RoomID(r.Id)
}

// MsgLoop goroutine dispatch messages.
func (r *Room) MsgLoop() {
	r.logger.Debugf("Room.MsgLoop() start: room=%v", r.Id)
Loop:
	for {
		select {
		case <-r.Done():
			r.logger.Infof("Room closed: room=%v", r.Id)
			break Loop
		case msg := <-r.msgCh:
			r.logger.Debugf("Room msg: room=%v, %T %v", r.Id, msg, msg)
			r.dispatch(msg)
		}
	}

	r.drainMsg()
	r.logger.Debugf("Room.MsgLoop() finish: room=%v", r.Id)
}

// drainMsg drain msgCh until all clients closed.
// clientのgoroutineがmsgChに書き込むところで停止するのを防ぐ
func (r *Room) drainMsg() {
	ch := make(chan struct{})
	go func() {
		r.wgClient.Wait()
		ch <- struct{}{}
	}()

	for {
		select {
		case msg := <-r.msgCh:
			r.logger.Debugf("Discard msg: room=%v %T %v", r.Id, msg, msg)
		case <-ch:
			return
		}
	}
}

// Done returns a channel which cloased when room is done.
func (r *Room) Done() <-chan struct{} {
	return r.done
}

// Post a mssage to room
func (r *Room) Post(m Msg) {
	r.msgCh <- m
}

func (r *Room) Timeout(c *Client) {
	r.removeClient(c, fmt.Errorf("client timeout: %v", c.Id))
}

func (r *Room) removeClient(c *Client, err error) {
	r.muClients.Lock()
	defer r.muClients.Unlock()

	cid := ClientID(c.Id)

	if _, ok := r.clients[cid]; !ok {
		r.logger.Debugf("Client may be aleady leaved: room=%v, client=%v", r.Id, cid)
		return
	}

	r.logger.Infof("Client removed: room=%v, client=%v", r.Id, cid)
	delete(r.clients, cid)
	r.leaved[cid] = err
	c.Removed()

	if len(r.clients) == 0 {
		close(r.done)
		return
	}

	r.Post(MsgLeave{c.ID()})
}

func (r *Room) dispatch(msg Msg) error {
	switch m := msg.(type) {
	case MsgCreate:
		return r.msgCreate(m)
	case MsgJoin:
		return r.msgJoin(m)
	default:
		return fmt.Errorf("unknown msg type: %T %v", m, m)
	}
}

func (r *Room) broadcast(ev Event) error {
	r.muClients.RLock()
	defer r.muClients.RUnlock()

	for _, c := range r.clients {
		if err := c.Send(ev); err != nil {
			// removeClient locks muClients so that must be called another goroutine.
			go r.removeClient(c, err)
		}
	}
	return nil
}

func (r *Room) msgCreate(msg MsgCreate) error {
	return r.broadcast(EvJoined{
		r.master.ClientInfo.Clone(),
	})
}

func (r *Room) msgJoin(msg MsgJoin) error {
	r.muClients.Lock()
	defer r.muClients.Unlock()

	if r.MaxPlayers == uint32(len(r.clients)) {
		close(msg.Res)
		return fmt.Errorf("Room full. max=%v, client=%v", r.MaxPlayers, msg.Info.Id)
	}

	r.wgClient.Add(1)
	c := NewClient(msg.Info, r)
	r.clients[ClientID(c.Id)] = c

	cinfo := c.ClientInfo.Clone()
	msg.Res <- JoinResponse{
		Room:   r.RoomInfo.Clone(),
		Client: cinfo,
	}
	r.broadcast(EvJoined{cinfo})

	return nil
}
