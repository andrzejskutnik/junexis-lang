package user

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"strconv"
	"strings"
	"sync"

	junexisStorage "junexis/server/junexis/storage"
	junexisRuntime "junexis/server/junexis/storage/runtime"
)

type homeObservation struct {
	ObservationID string `json:"observation_id"`
	Room          string `json:"room"`
	Status        string `json:"status"`
	Note          string `json:"note"`
	Sequence      int64  `json:"sequence"`
}

type homeStoreRuntime struct {
	mu       sync.Mutex
	store    junexisStorage.Store
	provider string
	restored bool
	openErr  error
}

var goldenHomeStore homeStoreRuntime

// HomeStore_save persists one domain observation through junexis.storage/0.1.
func HomeStore_save(value any) any {
	observation, err := decodeObservation(value)
	if err != nil {
		return map[string]any{"stored": false, "provider": providerName(), "key": ""}
	}
	store, _, openErr := goldenHomeStore.open()
	if openErr != nil {
		return map[string]any{"stored": false, "provider": providerName(), "key": observation.ObservationID}
	}
	payload, err := json.Marshal(observation)
	if err != nil {
		return map[string]any{"stored": false, "provider": providerName(), "key": observation.ObservationID}
	}
	storageErr := store.Put(context.Background(), observationBucket(observation.Room), observation.ObservationID, payload)
	return map[string]any{
		"stored":   storageErr == nil,
		"provider": providerName(),
		"key":      observation.ObservationID,
	}
}

// HomeStore_snapshot reads panel data through the same domain boundary used by server tasks.
func HomeStore_snapshot(room any, limit any) any {
	store, provider, openErr := goldenHomeStore.open()
	if openErr != nil {
		return snapshot(provider, false, nil)
	}
	records, storageErr := store.List(context.Background(), observationBucket(fmt.Sprint(room)))
	if storageErr != nil {
		return snapshot(provider, goldenHomeStore.wasRestored(), nil)
	}
	maximum := int(asInt64(limit))
	if maximum <= 0 || maximum > len(records) {
		maximum = len(records)
	}
	if len(records) > maximum {
		records = records[len(records)-maximum:]
	}
	observations := make([]homeObservation, 0, len(records))
	for _, record := range records {
		var observation homeObservation
		if err := json.Unmarshal(record.Payload, &observation); err == nil {
			observations = append(observations, observation)
		}
	}
	return snapshot(provider, goldenHomeStore.wasRestored(), observations)
}

// HomeStore_rollback_probe proves that an aborted transaction leaves no visible record.
func HomeStore_rollback_probe(value any) bool {
	observation, err := decodeObservation(value)
	if err != nil {
		return false
	}
	store, _, openErr := goldenHomeStore.open()
	if openErr != nil {
		return false
	}
	payload, err := json.Marshal(observation)
	if err != nil {
		return false
	}
	probeKey := "rollback-" + observation.ObservationID
	rollback := store.Transact(context.Background(), func(transaction junexisStorage.Transaction) *junexisStorage.Error {
		if storageErr := transaction.Put(context.Background(), observationBucket(observation.Room), probeKey, payload); storageErr != nil {
			return storageErr
		}
		return &junexisStorage.Error{Code: junexisStorage.ErrorConflict, Operation: "golden-rollback-probe", Outcome: "known"}
	})
	if rollback == nil {
		return false
	}
	_, getErr := store.Get(context.Background(), observationBucket(observation.Room), probeKey)
	return getErr != nil && getErr.Code == junexisStorage.ErrorNotFound
}

// HomeStore_health_check reports whether the selected provider is ready.
func HomeStore_health_check() bool {
	store, _, err := goldenHomeStore.open()
	return err == nil && store.Health(context.Background()) == nil
}

func (runtime *homeStoreRuntime) open() (junexisStorage.Store, string, error) {
	runtime.mu.Lock()
	defer runtime.mu.Unlock()
	if runtime.store != nil || runtime.openErr != nil {
		return runtime.store, runtime.provider, runtime.openErr
	}
	runtime.provider = providerName()
	ctx := context.Background()
	provider, storageErr := junexisRuntime.OpenFromEnvironment(ctx)
	if storageErr != nil {
		runtime.openErr = storageErr
		return nil, runtime.provider, runtime.openErr
	}
	runtime.store = provider
	marker, markerErr := runtime.store.Get(ctx, "junexis-meta", "process-marker")
	runtime.restored = markerErr == nil && len(marker.Payload) > 0
	processMarker := []byte(strconv.Itoa(os.Getpid()))
	if storageErr := runtime.store.Put(ctx, "junexis-meta", "process-marker", processMarker); storageErr != nil {
		runtime.openErr = storageErr
		_ = runtime.store.Close()
		runtime.store = nil
		return nil, runtime.provider, runtime.openErr
	}
	return runtime.store, runtime.provider, nil
}

func (runtime *homeStoreRuntime) wasRestored() bool {
	runtime.mu.Lock()
	defer runtime.mu.Unlock()
	return runtime.restored
}

func decodeObservation(value any) (homeObservation, error) {
	payload, err := json.Marshal(value)
	if err != nil {
		return homeObservation{}, err
	}
	var observation homeObservation
	if err := json.Unmarshal(payload, &observation); err != nil {
		return homeObservation{}, err
	}
	if strings.TrimSpace(observation.ObservationID) == "" || strings.TrimSpace(observation.Room) == "" {
		return homeObservation{}, fmt.Errorf("observation_id and room are required")
	}
	return observation, nil
}

func snapshot(provider string, restored bool, observations []homeObservation) map[string]any {
	if observations == nil {
		observations = []homeObservation{}
	}
	return map[string]any{
		"provider":               provider,
		"restored_after_restart": restored,
		"count":                  len(observations),
		"latest":                 observations,
		"build_id":               os.Getenv("JUNEXIS_BUILD_ID"),
		"bundle_id":              os.Getenv("JUNEXIS_BUNDLE_ID"),
	}
}

func observationBucket(room string) string {
	return "home-observations:" + strings.TrimSpace(room)
}

func providerName() string {
	selected := strings.ToLower(strings.TrimSpace(os.Getenv("JUNEXIS_STORAGE_PROVIDER")))
	if selected == "memory" {
		return "memory"
	}
	if selected == "postgresql" || selected == "postgres" {
		return "postgresql"
	}
	return "sqlite"
}

func asInt64(value any) int64 {
	switch typed := value.(type) {
	case int64:
		return typed
	case int:
		return int64(typed)
	case float64:
		return int64(typed)
	default:
		parsed, _ := strconv.ParseInt(fmt.Sprint(value), 10, 64)
		return parsed
	}
}
