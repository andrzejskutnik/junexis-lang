# JunexisLang Snippet Library

**Purpose:** Reusable device reference snippets for common hardware components.  
**Location:** `compiler/snippets/`  
**Format:** `.snippet.jnx` files with metadata and reference sections

## Structure

Each snippet file contains:
1. **Metadata** (name, description, author, version, tags)
2. **Parameters** (configurable fields with defaults)
3. **Reference Section** (@c/c++, @rust, @micropython)

## Usage

**In JunexisLang source:**

```junexis
device sensor_kitchen @embedded(esp32c3) {
  reference {
    @c/c++ {
      import: "dht22_adafruit"  // Load snippet from library
    }
  }
  hardware {
    dht22_pin: u8 : 4  // Override snippet parameter
  }
}
```

**Compiler behavior:**

1. Find snippet: `compiler/snippets/dht22_adafruit.snippet.jnx`
2. Load reference section (@c/c++ block)
3. Merge with device declaration
4. Interpolate parameters: `{hardware.dht22_pin}` → `4`
5. Generate C++ code

## Snippet Format

```junexis
// ===== SNIPPET METADATA =====
snippet dht22_adafruit {
  name: "DHT22 Temperature Sensor (Adafruit)"
  description: "Adafruit DHT library for DHT22/AM2302 temperature and humidity sensor"
  author: "JunexisLang Team"
  version: "1.0.0"
  tags: ["temperature", "humidity", "sensor", "dht22", "adafruit"]
  
  // Configurable parameters (with defaults)
  parameters {
    pin: u8 : 4               // GPIO pin (default: 4)
    sensor_type: string : "DHT22"  // DHT11 or DHT22 (default: DHT22)
  }
}

// ===== DEVICE REFERENCE =====
device dht22_adafruit_template @embedded {
  reference {
    @c/c++ "DHT22 Temperature Sensor" {
      library: "adafruit/DHT-sensor-library";
      include: <DHT.h>;
      declare: DHT dht;
      setup: dht.begin({hardware.pin}, {hardware.sensor_type});
    }
    
    @micropython "DHT22 via machine.Pin" {
      import: from machine import Pin;
      import: import dht;
      declare: sensor = dht.DHT22(Pin({hardware.pin}));
      init: # No initialization needed;
    }
  }
}
```

## Available Snippets

### Sensors

- **dht22_adafruit.snippet.jnx** - DHT22/AM2302 temperature/humidity (Adafruit)
- **bme280_adafruit.snippet.jnx** - BME280 pressure/temp/humidity (Adafruit)
- **pir_motion.snippet.jnx** - PIR motion sensor (generic)
- **ultrasonic_hcsr04.snippet.jnx** - HC-SR04 ultrasonic distance sensor

### Displays

- **ssd1306_adafruit.snippet.jnx** - SSD1306 128x64 OLED (Adafruit)
- **ili9341_bodmer.snippet.jnx** - ILI9341 TFT display (Bodmer TFT_eSPI)
- **lcd_i2c.snippet.jnx** - LCD 16x2/20x4 I2C (LiquidCrystal_I2C)

### Communication

- **uart_serial.snippet.jnx** - UART serial communication
- **i2c_wire.snippet.jnx** - I2C Wire library
- **spi_generic.snippet.jnx** - SPI communication

### Actuators

- **servo_standard.snippet.jnx** - Standard servo motor (Servo library)
- **stepper_accelstepper.snippet.jnx** - Stepper motor (AccelStepper)
- **relay_generic.snippet.jnx** - Relay module (generic digital output)

## Compiler Integration

**File:** `compiler/JunexisLang.Compiler.CodeGen/SnippetLoader.cs`

```csharp
public class SnippetLoader
{
    private readonly string _snippetPath;
    private readonly Dictionary<string, Snippet> _cache;

    public Snippet LoadSnippet(string name)
    {
        // Load from: compiler/snippets/{name}.snippet.jnx
        // Parse metadata + reference section
        // Cache for performance
    }

    public DeviceReferenceSection MergeWithDevice(
        Snippet snippet,
        DeviceDeclaration device)
    {
        // Merge snippet reference with device reference
        // Override parameters from device.hardware/device.network
        // Return merged reference section
    }
}
```

## Benefits

1. **Code Reuse**: Write once, use everywhere
2. **Best Practices**: Vendor-recommended configurations
3. **Multi-Language**: Same snippet for C++, Rust, MicroPython
4. **Versioning**: Track library version compatibility
5. **Documentation**: Inline comments + parameter descriptions

## Future Extensions

- **Snippet validation**: Test snippets with PlatformIO
- **Snippet registry**: Online repository with search
- **Auto-discovery**: Scan Arduino/PlatformIO libraries
- **Conflict resolution**: Handle multiple snippets with same library
- **Snippet composition**: Combine multiple snippets (e.g., DHT22 + SSD1306)

## Example: Smart Home System

```junexis
device sensor_kitchen @embedded(esp32c3) {
  reference {
    @c/c++ {
      import: "dht22_adafruit"       // Temperature sensor
      import: "ssd1306_adafruit"     // OLED display
    }
  }
  hardware {
    dht22_pin: u8 : 4
    oled_address: u8 : 0x3C
  }
}

device sensor_bedroom @embedded(esp32c3) {
  reference {
    @c/c++ {
      import: "pir_motion"           // Motion sensor
      import: "relay_generic"        // Light relay
    }
  }
  hardware {
    pir_pin: u8 : 14
    relay_pin: u8 : 27
  }
}
```

**Generated C++ (sensor_kitchen):**

```cpp
// ===== INCLUDES (from snippets) =====
#include <DHT.h>
#include <Adafruit_SSD1306.h>

// ===== GLOBAL DECLARATIONS =====
DHT dht;
Adafruit_SSD1306 display(128, 64, &Wire, -1);

void setup() {
    Serial.begin(115200);
    
    // ===== INITIALIZATION =====
    dht.begin(4, DHT22);
    display.begin(SSD1306_SWITCHCAPVCC, 0x3C);
}
```

## Version

- **Created:** 2025-11-06
- **Status:** Design phase (not yet implemented)
- **Priority:** HIGH (Step 4 after codegen proof-of-concept)
