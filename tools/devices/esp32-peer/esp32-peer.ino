// esp32-peer -- the Bluetooth peer the device fixtures talk to (JGraph device classes plan, stage D3).
//
// The board is what jgraph.internal.btsim simulates in JGraph (src/JGraph.Devices/Simulation/
// SimulatedBluetooth.cs), so a fixture recorded in R2025b against the board replays against the
// simulator:
//
//   * Bluetooth classic, "JGraphPeer": a serial port (SPP) channel running the in-band protocol of
//     src/JGraph.Devices/Simulation/PeerEngine.cs. Bytes are data (logged, echoed when echo is on,
//     matched against the reply rules); a command is ESC ESC '{' text '}':
//       send HEX | later MS HEX | chunks MS N HEX | echo on|off | recv | status | on HEX HEX | reset
//     An unknown or malformed command answers '?'. status answers "0000" and the breaks (always 0000):
//     an RFCOMM channel has no modem pins.
//   * Bluetooth Low Energy, "JGraphPeer", in this order:
//       180F Battery Service: 2A19 Battery Level (Read, Notify) = 100
//       180D Heart Rate: 2A37 Heart Rate Measurement (Notify) = [0 60+k] every 100 ms while subscribed,
//            k counting from 0 and wrapping at 40; 2A38 Body Sensor Location (Read) = 1
//       FFE0: FFE1 (Read, WriteWithoutResponse, Write, Notify), descriptors 2901 "JGraph echo" and
//            2902; a write becomes the value and is notified back while notifications are on.
//
// Needs the original ESP32 (the S2, S3, C3 and C6 have no Bluetooth classic). Arduino-ESP32 core 2.x
// or 3.x, board "ESP32 Dev Module", Partition Scheme "Huge APP (3MB No OTA)": the two stacks together
// do not fit the default partition. See README.md.

#include <BluetoothSerial.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>

static const char *kName = "JGraphPeer";

// ---------------------------------------------------------------------------------------------------
// Bluetooth classic: the peer engine
// ---------------------------------------------------------------------------------------------------

BluetoothSerial spp;

struct Rule {
  std::vector<uint8_t> match;
  std::vector<uint8_t> reply;
};

struct Pending {
  uint32_t due;
  std::vector<uint8_t> bytes;
};

static std::vector<uint8_t> logBytes;
static std::vector<uint8_t> window;
static std::vector<Rule> rules;
static std::vector<Pending> pending;
static std::string command;
static int held = 0;             // how many of ESC ESC '{' have been seen
static bool inCommand = false;
static bool echoOn = false;

static const uint8_t kStart[3] = {0x1B, 0x1B, '{'};

static void sendBytes(const std::vector<uint8_t> &bytes) {
  if (!bytes.empty()) {
    spp.write(bytes.data(), bytes.size());
  }
}

static void sendText(const char *text) {
  spp.write(reinterpret_cast<const uint8_t *>(text), strlen(text));
}

static bool parseHex(const std::string &word, std::vector<uint8_t> &out) {
  out.clear();
  if (word.size() % 2 != 0) {
    return false;
  }
  for (size_t i = 0; i < word.size(); i += 2) {
    char pair[3] = {word[i], word[i + 1], 0};
    char *end = nullptr;
    long value = strtol(pair, &end, 16);
    if (end != pair + 2) {
      return false;
    }
    out.push_back(static_cast<uint8_t>(value));
  }
  return true;
}

static bool parseInt(const std::string &word, long &out) {
  if (word.empty()) {
    return false;
  }
  char *end = nullptr;
  out = strtol(word.c_str(), &end, 10);
  return *end == 0 && out >= 0;
}

static void schedule(uint32_t ms, const std::vector<uint8_t> &bytes) {
  pending.push_back({millis() + ms, bytes});
}

static void resetEngine() {
  logBytes.clear();
  window.clear();
  rules.clear();
  pending.clear();
  echoOn = false;
}

static void refuse() { sendText("?"); }

static void runCommand(const std::string &text) {
  std::vector<std::string> words;
  size_t at = 0;
  while (at < text.size()) {
    while (at < text.size() && text[at] == ' ') at++;
    size_t start = at;
    while (at < text.size() && text[at] != ' ') at++;
    if (at > start) words.push_back(text.substr(start, at - start));
  }
  if (words.empty()) {
    refuse();
    return;
  }

  std::string verb = words[0];
  for (auto &c : verb) c = tolower(c);
  std::vector<uint8_t> bytes;
  long n = 0, m = 0;

  if (verb == "send") {
    if (words.size() > 1 && !parseHex(words[1], bytes)) return refuse();
    sendBytes(bytes);
  } else if (verb == "later") {
    if (words.size() < 2 || !parseInt(words[1], n)) return refuse();
    if (words.size() > 2 && !parseHex(words[2], bytes)) return refuse();
    schedule(n, bytes);
  } else if (verb == "chunks") {
    if (words.size() < 3 || !parseInt(words[1], n) || !parseInt(words[2], m)) return refuse();
    if (words.size() > 3 && !parseHex(words[3], bytes)) return refuse();
    size_t size = m < 1 ? 1 : static_cast<size_t>(m);
    int k = 1;
    for (size_t i = 0; i < bytes.size(); i += size, k++) {
      size_t len = std::min(size, bytes.size() - i);
      schedule(n * k, std::vector<uint8_t>(bytes.begin() + i, bytes.begin() + i + len));
    }
  } else if (verb == "echo") {
    std::string arg = words.size() > 1 ? words[1] : "";
    for (auto &c : arg) c = tolower(c);
    echoOn = arg == "on";
  } else if (verb == "recv") {
    char head[9];
    snprintf(head, sizeof head, "%08X", static_cast<unsigned>(logBytes.size()));
    std::string reply(head);
    char pair[3];
    for (uint8_t b : logBytes) {
      snprintf(pair, sizeof pair, "%02X", b);
      reply += pair;
    }
    logBytes.clear();
    spp.write(reinterpret_cast<const uint8_t *>(reply.data()), reply.size());
  } else if (verb == "status") {
    sendText("00000000");
  } else if (verb == "pins" || verb == "break") {
    // An RFCOMM channel has no modem pins and no break: accepted and ignored.
  } else if (verb == "on") {
    Rule rule;
    if (words.size() < 2 || !parseHex(words[1], rule.match)) return refuse();
    if (words.size() > 2 && !parseHex(words[2], rule.reply)) return refuse();
    rules.push_back(rule);
  } else if (verb == "reset") {
    resetEngine();
  } else if (verb == "quit") {
    // Nothing to quit on a board.
  } else {
    refuse();
  }
}

static bool endsWith(const std::vector<uint8_t> &data, const std::vector<uint8_t> &tail) {
  if (tail.empty() || data.size() < tail.size()) return false;
  return std::equal(tail.begin(), tail.end(), data.end() - tail.size());
}

static void dataByte(uint8_t b) {
  logBytes.push_back(b);
  window.push_back(b);
  if (window.size() > 4096) window.erase(window.begin(), window.begin() + 2048);
  if (echoOn) spp.write(&b, 1);
  for (const Rule &rule : rules) {
    if (endsWith(window, rule.match)) sendBytes(rule.reply);
  }
}

static void step(uint8_t b) {
  if (inCommand) {
    if (b == '}') {
      inCommand = false;
      std::string text = command;
      command.clear();
      runCommand(text);
    } else if (command.size() < 4096) {
      command.push_back(static_cast<char>(b));
    }
    return;
  }
  if (b == kStart[held]) {
    if (++held == 3) {
      held = 0;
      inCommand = true;
    }
    return;
  }
  if (held > 0) {
    int was = held;
    held = 0;
    for (int i = 0; i < was; i++) dataByte(kStart[i]);
    step(b);
    return;
  }
  dataByte(b);
}

// ---------------------------------------------------------------------------------------------------
// Bluetooth Low Energy: the GATT server
// ---------------------------------------------------------------------------------------------------

static BLECharacteristic *heartRate = nullptr;
static BLECharacteristic *echoChar = nullptr;
static BLE2902 *heartRateConfig = nullptr;
static BLE2902 *echoConfig = nullptr;
static uint32_t nextBeat = 0;
static int beat = 0;

class EchoCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic *characteristic) override {
    // The written bytes are already the value; notify them back while notifications are on.
    if (echoConfig != nullptr && echoConfig->getNotifications()) {
      characteristic->notify();
    }
  }
};

class ServerCallbacks : public BLEServerCallbacks {
  void onDisconnect(BLEServer *server) override {
    beat = 0;
    BLEDevice::startAdvertising();
  }
};

static void startBle() {
  BLEDevice::init(kName);
  BLEServer *server = BLEDevice::createServer();
  server->setCallbacks(new ServerCallbacks());

  BLEService *battery = server->createService(BLEUUID((uint16_t)0x180F));
  BLECharacteristic *level = battery->createCharacteristic(BLEUUID((uint16_t)0x2A19),
      BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY);
  uint8_t full = 100;
  level->setValue(&full, 1);
  level->addDescriptor(new BLE2902());

  BLEService *heart = server->createService(BLEUUID((uint16_t)0x180D));
  heartRate = heart->createCharacteristic(BLEUUID((uint16_t)0x2A37), BLECharacteristic::PROPERTY_NOTIFY);
  heartRateConfig = new BLE2902();
  heartRate->addDescriptor(heartRateConfig);
  BLECharacteristic *location = heart->createCharacteristic(BLEUUID((uint16_t)0x2A38), BLECharacteristic::PROPERTY_READ);
  uint8_t chest = 1;
  location->setValue(&chest, 1);

  BLEService *custom = server->createService(BLEUUID((uint16_t)0xFFE0));
  echoChar = custom->createCharacteristic(BLEUUID((uint16_t)0xFFE1),
      BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_WRITE_NR |
      BLECharacteristic::PROPERTY_WRITE | BLECharacteristic::PROPERTY_NOTIFY);
  echoChar->setCallbacks(new EchoCallbacks());
  BLEDescriptor *description = new BLEDescriptor(BLEUUID((uint16_t)0x2901));
  description->setValue("JGraph echo");
  echoChar->addDescriptor(description);
  echoConfig = new BLE2902();
  echoChar->addDescriptor(echoConfig);

  battery->start();
  heart->start();
  custom->start();

  BLEAdvertising *advertising = BLEDevice::getAdvertising();
  BLEAdvertisementData data;
  data.setFlags(0x06);
  data.setCompleteServices(BLEUUID((uint16_t)0x180F));
  data.setManufacturerData(std::string("\xE5\x02\x4A\x47", 4));
  advertising->setAdvertisementData(data);
  BLEAdvertisementData response;
  response.setName(kName);
  advertising->setScanResponseData(response);
  advertising->addServiceUUID(BLEUUID((uint16_t)0x180D));
  advertising->addServiceUUID(BLEUUID((uint16_t)0xFFE0));
  BLEDevice::startAdvertising();
}

static void heartBeat() {
  if (heartRate == nullptr || heartRateConfig == nullptr || !heartRateConfig->getNotifications()) {
    beat = 0;
    return;
  }
  uint32_t now = millis();
  if (static_cast<int32_t>(now - nextBeat) < 0) return;
  nextBeat = now + 100;
  uint8_t value[2] = {0, static_cast<uint8_t>(60 + beat)};
  beat = (beat + 1) % 40;
  heartRate->setValue(value, 2);
  heartRate->notify();
}

// ---------------------------------------------------------------------------------------------------

void setup() {
  Serial.begin(115200);
  spp.begin(kName);
  startBle();
  Serial.println("JGraphPeer ready: SPP and BLE advertising");
}

void loop() {
  while (spp.available()) {
    step(static_cast<uint8_t>(spp.read()));
  }

  uint32_t now = millis();
  for (size_t i = 0; i < pending.size();) {
    if (static_cast<int32_t>(now - pending[i].due) >= 0) {
      sendBytes(pending[i].bytes);
      pending.erase(pending.begin() + i);
    } else {
      i++;
    }
  }

  heartBeat();
  delay(1);
}
