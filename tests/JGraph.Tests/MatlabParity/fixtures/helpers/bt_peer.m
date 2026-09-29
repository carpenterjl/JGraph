function bt_peer()
% BT_PEER  The Bluetooth the fixtures talk to (device classes plan, stage D3). In JGraph,
%   jgraph.internal.btsim installs the simulated radio with the classic device and the peripheral
%   JGraphPeer. In MATLAB the system's radio answers, which must be on; the fixtures that connect
%   need the ESP32 running tools/devices/esp32-peer, paired as JGraphPeer.
try
    jgraph.internal.btsim('on');
catch
end
end
