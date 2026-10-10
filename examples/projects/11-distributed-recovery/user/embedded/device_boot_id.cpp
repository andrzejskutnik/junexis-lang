#include <Arduino.h>
#include <esp_system.h>
#include <string>

std::string device_boot_id() {
    static const std::string boot_id = "esp32-" + std::to_string(esp_random());
    return boot_id;
}
