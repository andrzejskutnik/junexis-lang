package main

import (
	"context"
	"os"
	"testing"

	storageRuntime "junexis/server/junexis/storage/runtime"
)

func TestJunexisTaskExecutesNamedQuery(t *testing.T) {
	ctx := context.Background()
	provider, storageErr := storageRuntime.Open(ctx, storageRuntime.Config{
		HostPath:          os.Getenv("JUNEXIS_M13_HOST"),
		ProviderEntrypoint: os.Getenv("JUNEXIS_M13_PROVIDER"),
		ProviderID:        "memory",
		BindingID:         "m13-10-golden",
	})
	if storageErr != nil {
		t.Fatal(storageErr, storageErr.Cause)
	}
	defer provider.Close()

	junexisTaskContext = ctx
	junexisStorageProvider = provider
	homeID := "4cc9687d-bc12-4e8b-b272-907bcd5ed36d"
	payload := []byte(`{"id":"8cc9687d-bc12-4e8b-b272-907bcd5ed36d","homeId":"` + homeID + `","sensor":"temperature","value":21.5,"recordedAt":"2026-08-23T10:00:00Z"}`)
	if storageErr := provider.Put(ctx, "Observation", "observation-1", payload); storageErr != nil {
		t.Fatal(storageErr, storageErr.Cause)
	}

	rows := ReadHome(homeID)
	if len(rows) != 1 {
		t.Fatalf("got %d rows, want 1", len(rows))
	}
	if rows[0].SensorName != "temperature" || rows[0].Value != 21.5 {
		t.Fatalf("unexpected row: %#v", rows[0])
	}
	if rows[0].RecordedAt.IsZero() {
		t.Fatal("task-facing timestamp was not converted to time.Time")
	}
}
