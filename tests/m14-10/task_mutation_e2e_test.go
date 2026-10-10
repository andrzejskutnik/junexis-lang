package main

import (
	"context"
	"encoding/json"
	"os"
	"testing"
	"time"

	storageRuntime "junexis/server/junexis/storage/runtime"
)

func TestJunexisTaskExecutesNamedMutation(t *testing.T) {
	ctx := context.Background()
	provider, storageErr := storageRuntime.Open(ctx, storageRuntime.Config{
		HostPath: os.Getenv("JUNEXIS_M14_HOST"), ProviderEntrypoint: os.Getenv("JUNEXIS_M14_PROVIDER"),
		ProviderID: "memory", BindingID: "m14-10-golden",
	})
	if storageErr != nil { t.Fatal(storageErr, storageErr.Cause) }
	defer provider.Close()

	junexisTaskContext = ctx
	junexisStorageProvider = provider
	id := "8cc9687d-bc12-4e8b-b272-907bcd5ed36d"
	result := RegisterObservation(
		id, "4cc9687d-bc12-4e8b-b272-907bcd5ed36d", "temperature", 21.5,
		time.Date(2026, 8, 24, 10, 0, 0, 0, time.UTC),
	)
	if result.Outcome != "applied" || result.AffectedRows != 1 { t.Fatalf("unexpected task result: %#v", result) }

	records, storageErr := provider.List(ctx, "Observation")
	if storageErr != nil { t.Fatal(storageErr, storageErr.Cause) }
	if len(records) != 1 { t.Fatalf("got %d records, want 1", len(records)) }
	var payload map[string]any
	if err := json.Unmarshal(records[0].Payload, &payload); err != nil { t.Fatal(err) }
	if payload["id"] != id || payload["sensor"] != "temperature" { t.Fatalf("unexpected stored payload: %#v", payload) }
}
