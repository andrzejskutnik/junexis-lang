package user

import "sync"

// storage TemperatureStore
//
// This file is developer-owned. Junexis generates the server architecture and
// calls these functions through the extern boundary, but this implementation is
// intentionally normal Go code kept under source control.
var (
	temperatureStoreMu       sync.Mutex
	temperatureStoreReadings []any
)

func TemperatureStore_save(reading any) {
	temperatureStoreMu.Lock()
	defer temperatureStoreMu.Unlock()

	temperatureStoreReadings = append(temperatureStoreReadings, reading)
	if len(temperatureStoreReadings) > 256 {
		temperatureStoreReadings = temperatureStoreReadings[len(temperatureStoreReadings)-256:]
	}
}

func TemperatureStore_recent(limit int64) []any {
	temperatureStoreMu.Lock()
	defer temperatureStoreMu.Unlock()

	if limit <= 0 || int(limit) > len(temperatureStoreReadings) {
		limit = int64(len(temperatureStoreReadings))
	}

	start := len(temperatureStoreReadings) - int(limit)
	result := make([]any, int(limit))
	copy(result, temperatureStoreReadings[start:])
	return result
}

