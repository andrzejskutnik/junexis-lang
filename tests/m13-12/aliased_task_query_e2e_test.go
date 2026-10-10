package main

import (
	"context"
	"os"
	"testing"

	storageRuntime "junexis/server/junexis/storage/runtime"
)

func TestJunexisTaskExecutesAliasedNamedQuery(t *testing.T) {
	ctx := context.Background()
	provider, storageErr := storageRuntime.Open(ctx, storageRuntime.Config{
		HostPath:           os.Getenv("JUNEXIS_M13_HOST"),
		ProviderEntrypoint: os.Getenv("JUNEXIS_M13_PROVIDER"),
		ProviderID:         "memory",
		BindingID:          "m13-12-golden",
	})
	if storageErr != nil {
		t.Fatal(storageErr, storageErr.Cause)
	}
	defer provider.Close()

	junexisTaskContext = ctx
	junexisStorageProvider = provider
	homeID := "6cc9687d-bc12-4e8b-b272-907bcd5ed36d"
	payload := []byte(`{"id":"acc9687d-bc12-4e8b-b272-907bcd5ed36d","homeId":"` + homeID + `","sensor":"air-quality","value":71.0,"recordedAt":"2026-08-23T14:00:00Z"}`)
	if storageErr := provider.Put(ctx, "Observation", "observation-aliased-1", payload); storageErr != nil {
		t.Fatal(storageErr, storageErr.Cause)
	}

	rows := ReadHome(homeID)
	if len(rows) != 1 {
		t.Fatalf("got %d rows, want 1", len(rows))
	}
	if rows[0].SensorName != "air-quality" || rows[0].Value != 71.0 {
		t.Fatalf("unexpected row: %#v", rows[0])
	}
	if rows[0].RecordedAt.IsZero() {
		t.Fatal("task-facing timestamp was not converted to time.Time")
	}
}
