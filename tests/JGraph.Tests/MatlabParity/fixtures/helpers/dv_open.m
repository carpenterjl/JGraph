function s = dv_open(varargin)
% DV_OPEN  The device fixtures' start: the peer on the far end of COM20 (device_peer), no saved
%   serialport preferences, and s = serialport("COM20", 9600, "Timeout", 1, varargin{:}) with the
%   peer reset and both buffers empty.
device_peer();
internal.Serialport.clearPreferences();
s = serialport("COM20", 9600, "Timeout", 1, varargin{:});
dp(s, 'reset');
flush(s);
end
