package webui

import (
	"bytes"
	"encoding/json"
	"io"
	"net/http"
	"testing"
)

// getJSON fetches a URL and decodes the JSON object at the top level.
func getJSON(t *testing.T, raw string) map[string]any {
	t.Helper()
	status, body := getPair(t, raw)
	if status != http.StatusOK {
		t.Fatalf("GET %s = %d (%#v)", raw, status, body)
	}
	return body
}

func getPair(t *testing.T, raw string) (int, map[string]any) {
	t.Helper()
	res, err := http.Get(raw)
	if err != nil {
		t.Fatal(err)
	}
	defer res.Body.Close()
	data, _ := io.ReadAll(res.Body)
	var body map[string]any
	_ = json.Unmarshal(data, &body)
	return res.StatusCode, body
}

func postJSON(t *testing.T, raw string, payload any) (int, map[string]any) {
	t.Helper()
	data, _ := json.Marshal(payload)
	res, err := http.Post(raw, "application/json", bytes.NewReader(data))
	if err != nil {
		t.Fatal(err)
	}
	defer res.Body.Close()
	bodyData, _ := io.ReadAll(res.Body)
	var body map[string]any
	_ = json.Unmarshal(bodyData, &body)
	return res.StatusCode, body
}
