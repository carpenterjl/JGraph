function out = dp(s, cmd)
% DP  Send one command to the device on the far end of serialport s (see device_peer) and answer its
%   reply: 'recv' answers the data bytes it received since the last recv as a double row, 'status'
%   the eight characters of its pin and break status; every other command answers [].
%   The command travels in band (ESC ESC { text }), so it is never data. Reply commands flush the
%   input first, so a stray byte cannot shift the reply.
cmd = char(cmd);
verb = strtok(cmd);
if any(strcmp(verb, {'recv', 'status'}))
    flush(s, "input");
end
write(s, [27 27 double('{') double(cmd) double('}')], "uint8");
switch verb
    case 'recv'
        n = hex2dec(read(s, 8, "char"));
        if n == 0
            out = zeros(1, 0);
        else
            out = hex2dec(reshape(read(s, 2 * n, "char"), 2, [])')';
        end
    case 'status'
        out = read(s, 8, "char");
    otherwise
        out = [];
end
end
