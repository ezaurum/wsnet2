package pb

import (
	"database/sql/driver"
	"fmt"
	"time"

	"github.com/golang/protobuf/ptypes"
	timestamp "github.com/golang/protobuf/ptypes/timestamp"
	"github.com/vmihailenco/msgpack/v4"
)

func (ts *Timestamp) Scan(val interface{}) error {
	t, ok := val.(time.Time)
	if !ok {
		return fmt.Errorf("type is not date.Time: %T, %v", val, val)
	}
	var err error
	ts.Timestamp, err = ptypes.TimestampProto(t)
	return err
}

func (ts Timestamp) Value() (driver.Value, error) {
	return ptypes.Timestamp(ts.Timestamp)
}

func (ts Timestamp) Time() time.Time {
	t, _ := ptypes.Timestamp(ts.Timestamp)
	return t
}

func (ts *Timestamp) EncodeMsgpack(enc *msgpack.Encoder) error {
	return enc.Encode(ts.Timestamp.Seconds)
}

func (ts *Timestamp) DecodeMsgpack(dec *msgpack.Decoder) error {
	ts.Timestamp = &timestamp.Timestamp{}
	return dec.Decode(&ts.Timestamp.Seconds)
}

var _ msgpack.CustomEncoder = (*Timestamp)(nil)
var _ msgpack.CustomDecoder = (*Timestamp)(nil)
