package auth

import (
	"encoding/binary"
	"testing"
	"time"
)

func TestAuth(t *testing.T) {
	userId := []byte("alice")
	timestamp := make([]byte, 8)
	binary.BigEndian.PutUint64(timestamp, uint64(time.Now().Unix()))
	key := "hoge"
	nonce, _ := GenerateNonce()

	mac := CalculateHMAC([]byte(key), userId, timestamp, nonce)
	if !ValidHMAC(mac, []byte(key), userId, timestamp, nonce) {
		t.Fatalf("invalid hmac")
	}
	hm := CalculateHexHMAC([]byte(key), userId, timestamp, nonce)
	if !ValidHexHMAC(hm, []byte(key), userId, timestamp, nonce) {
		t.Fatalf("invalid hex hmac")
	}

	invalidKey := "fuga"
	if ValidHMAC(mac, []byte(invalidKey), userId, timestamp, nonce) {
		t.Fatalf("invalid result")
	}
	if ValidHexHMAC(hm, []byte(invalidKey), userId, timestamp, nonce) {
		t.Fatalf("invalid result")
	}

	invalidUserId := []byte("bob")
	if ValidHMAC(mac, []byte(key), invalidUserId, timestamp, nonce) {
		t.Fatalf("invalid result")
	}
	if ValidHexHMAC(hm, []byte(key), invalidUserId, timestamp, nonce) {
		t.Fatalf("invalid result")
	}

	invalidTimestamp := make([]byte, 8)
	binary.BigEndian.PutUint64(invalidTimestamp, uint64(time.Now().Unix()+30))
	if ValidHMAC(mac, []byte(key), userId, invalidTimestamp, nonce) {
		t.Fatalf("invalid result")
	}
	if ValidHexHMAC(hm, []byte(key), userId, invalidTimestamp, nonce) {
		t.Fatalf("invalid result")
	}

	invalidNonce, _ := GenerateNonce()
	if ValidHMAC(mac, []byte(key), userId, timestamp, invalidNonce) {
		t.Fatalf("invalid result")
	}
	if ValidHexHMAC(hm, []byte(key), userId, timestamp, invalidNonce) {
		t.Fatalf("invalid result")
	}
}

func TestAuthData(t *testing.T) {
	key := "testappkey"
	userId := "user001"
	now := time.Now()

	data, err := GenerateAuthData(key, userId, now)
	if err != nil {
		t.Fatalf("%+v", err)
	}

	err = ValidAuthData(data, key, userId, now.Truncate(time.Second))
	if err != nil {
		t.Fatalf("%+v", err)
	}

	err = ValidAuthData(data, key, userId, now.Add(time.Second))
	if err == nil {
		t.Fatalf("must be expired")
	}

	err = ValidAuthData(data, key, "user002", now.Add(-time.Second))
	if err == nil {
		t.Fatalf("other user must be error")
	}

	err = ValidAuthData(data, "otherkey", userId, now.Add(-time.Second))
	if err == nil {
		t.Fatalf("other kye must be error")
	}
}
