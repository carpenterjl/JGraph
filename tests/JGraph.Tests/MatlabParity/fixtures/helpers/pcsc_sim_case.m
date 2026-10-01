function out = pcsc_sim_case(kind)
% PCSC_SIM_CASE  The cases of pcsc_sim (device classes plan, stage D12, ADR 0196): jgraph.pcsc.readers,
%   connect, transmit, control, status, the transaction calls, reconnect, disconnect and watch on
%   jgraph.internal.pcscsim's readers. "JGraph Test Reader 0" holds the one card, which speaks T=1;
%   "JGraph Test Reader 1" is empty. The card's application is named A0 00 00 0F 4A 47 01: once
%   selected, GET DATA answers "JGraph" and 80 10 echoes its data; VERIFY takes the PIN 1234 and
%   blocks after three wrong ones. The answers are written from that rule. Answers text.
global DVLOG
DVLOG = {};
jgraph.internal.pcscsim('on');
r0 = 'JGraph Test Reader 0';
r1 = 'JGraph Test Reader 1';
select = '00 A4 04 00 07 A0 00 00 0F 4A 47 01';
getdata = [0 202 0 0 0];
try
    switch kind
        case 'readers'
            T = jgraph.pcsc.readers;
            out = sprintf('%s %d %d; %s; %s; %s; %s; %d %d %s; [%s] [%s]', class(T), size(T), strjoin(T.Properties.VariableNames, ' '), ...
                strjoin(T.Reader', ', '), strjoin(T.State', ' '), class(T.CardPresent), T.CardPresent, class(T.ATR), T.ATR(1), T.ATR(2));
        case 'readers_args'
            out = dv_err(@() jgraph.pcsc.readers(1));
        case 'connect'
            c = jgraph.pcsc.connect;
            out = sprintf('%s; %s; %s; %s; %s; %d %d %d', class(c), c.Reader, c.ATR, c.Protocol, c.Share, c.InTransaction, isvalid(c), isa(c, 'handle'));
        case 'disp'
            c = jgraph.pcsc.connect;
            out = ix_flat(evalc('disp(c)'));
        case 'properties'
            c = jgraph.pcsc.connect;
            out = [strjoin(properties(c)', ' ') ' // ' strjoin(methods(c)', ' ')];
        case 'select'
            c = jgraph.pcsc.connect;
            [r, sw] = transmit(c, select);
            out = sprintf('%s %d %d [%s] %s %s', class(r), size(r), sprintf('%d ', r), class(sw), sw);
        case 'get_data'
            c = jgraph.pcsc.connect;
            transmit(c, select);
            [r, sw] = transmit(c, getdata);
            one = transmit(c, getdata);
            out = sprintf('%s %s %s', char(r), sw, char(one));
        case 'echo'
            % The same command as numbers, as uint8, as hex text in three spellings, and by the dot.
            c = jgraph.pcsc.connect;
            transmit(c, select);
            a = transmit(c, [128 16 0 0 3 1 2 3]);
            b = transmit(c, uint8([128 16 0 0 2 254 255]));
            d = transmit(c, "80100000 01 7f");
            e = transmit(c, '0x80:0x10:0x00:0x00:0x01:0xAA');
            f = c.transmit([128 16 0 0 0]);
            out = sprintf('[%s] [%s] [%s] [%s] %d %d', sprintf('%d ', a), sprintf('%d ', b), sprintf('%d ', d), sprintf('%d ', e), size(f));
        case 'status_word'
            % A status the caller does not take is thrown; one it takes is answered.
            c = jgraph.pcsc.connect;
            thrown = dv_err(@() transmit(c, getdata));
            [r, sw] = transmit(c, getdata);
            [r2, sw2] = transmit(c, '00 A4 04 00 02 A0 01');
            [r3, sw3] = transmit(c, [0 176 0 0]);
            [r4, sw4] = transmit(c, [255 202 0 0]);
            out = sprintf('%s // %d %s %s %s %s %d', thrown, numel(r), sw, sw2, sw3, sw4, numel(r2) + numel(r3) + numel(r4));
        case 'pin'
            c = jgraph.pcsc.connect;
            wrong = '00 20 00 00 04 30 30 30 30';
            right = '00 20 00 00 04 31 32 33 34';
            [~, s1] = transmit(c, wrong);
            [~, s2] = transmit(c, right);
            [~, s3] = transmit(c, wrong);
            [~, s4] = transmit(c, wrong);
            thrown = dv_err(@() transmit(c, wrong));
            [~, s6] = transmit(c, right);
            out = sprintf('%s %s %s %s %s // %s', s1, s2, s3, s4, s6, thrown);
        case 'bad_apdu'
            c = jgraph.pcsc.connect;
            out = [dv_err(@() transmit(c, [0 202 0])) ' // ' dv_err(@() transmit(c, '00 CA 0')) ' // ' dv_err(@() transmit(c, 'hello')) ...
                ' // ' dv_err(@() transmit(c, [0 256 0 0])) ' // ' dv_err(@() transmit(c, [0 1.5 0 0])) ' // ' dv_err(@() transmit(c, {1})) ...
                ' // ' dv_err(@() transmit(c)) ' // ' dv_err(@() transmit(c, getdata, 1)) ' // ' pcsc_outputs(c, getdata)];
        case 'control'
            c = jgraph.pcsc.connect;
            a = control(c, jgraph.pcsc.ctlcode(3400));
            b = control(c, jgraph.pcsc.ctlcode(2048), [1 2 3]);
            d = control(c, '0x00312000', 'AA BB');
            e = control(c, "312000");
            out = sprintf('%s [%s] [%s] [%s] %d %d', class(a), sprintf('%d ', a), sprintf('%d ', b), sprintf('%d ', d), size(e));
        case 'control_bad'
            c = jgraph.pcsc.connect;
            out = [dv_err(@() control(c, 1)) ' // ' dv_err(@() control(c)) ' // ' dv_err(@() control(c, -1)) ' // ' dv_err(@() control(c, 'xyz')) ...
                ' // ' dv_err(@() control(c, 1, 2, 3)) ' // ' dv_err(@() control(c, jgraph.pcsc.ctlcode(2048), 300))];
        case 'ctlcode'
            out = [sprintf('%d %d %s // ', jgraph.pcsc.ctlcode(3400), jgraph.pcsc.ctlcode(0), class(jgraph.pcsc.ctlcode(1))) ...
                dv_err(@() jgraph.pcsc.ctlcode(4096)) ' // ' dv_err(@() jgraph.pcsc.ctlcode('a')) ' // ' dv_err(@() jgraph.pcsc.ctlcode())];
        case 'status'
            c = jgraph.pcsc.connect;
            s = status(c);
            out = sprintf('%s; %s; %s %s %s %s // %s', class(s), strjoin(fieldnames(s)', ' '), s.Reader, s.State, s.Protocol, s.ATR, dv_err(@() status(c, 1)));
        case 'by_name'
            a = jgraph.pcsc.connect(r0);
            n1 = a.Reader;
            clear a
            b = jgraph.pcsc.connect("reader 0");
            n2 = b.Reader;
            clear b
            d = jgraph.pcsc.connect('JGRAPH TEST READER 0');
            n3 = d.Reader;
            clear d
            e = jgraph.pcsc.connect(1);
            n4 = e.Reader;
            clear e
            T = jgraph.pcsc.readers;
            f = jgraph.pcsc.connect(T(1, :));
            out = sprintf('%s; %s; %s; %s; %s', n1, n2, n3, n4, f.Reader);
        case 'bad_reader'
            T = jgraph.pcsc.readers;
            out = [dv_err(@() jgraph.pcsc.connect('nope')) ' // ' dv_err(@() jgraph.pcsc.connect('JGraph')) ' // ' dv_err(@() jgraph.pcsc.connect(3)) ...
                ' // ' dv_err(@() jgraph.pcsc.connect(0)) ' // ' dv_err(@() jgraph.pcsc.connect({1})) ' // ' dv_err(@() jgraph.pcsc.connect(T)) ...
                ' // ' dv_err(@() jgraph.pcsc.connect('')) ' // ' dv_err(@() jgraph.pcsc.connect(r1))];
        case 'options'
            a = jgraph.pcsc.connect(r0, 'Share', 'exclusive', 'Protocol', 'T1');
            p1 = sprintf('%s %s', a.Share, a.Protocol);
            clear a
            b = jgraph.pcsc.connect(Protocol="t1", Share="SHARED");
            p2 = sprintf('%s %s', b.Share, b.Protocol);
            clear b
            d = jgraph.pcsc.connect(r0, 'sh', 'exclusive');
            out = sprintf('%s; %s; %s', p1, p2, d.Share);
        case 'options_bad'
            out = [dv_err(@() jgraph.pcsc.connect(r0, 'Nope', 1)) ' // ' dv_err(@() jgraph.pcsc.connect(r0, 'Share', 'mine')) ...
                ' // ' dv_err(@() jgraph.pcsc.connect(r0, 'Protocol', 2)) ' // ' dv_err(@() jgraph.pcsc.connect(r0, 'Protocol', 'T0')) ...
                ' // ' dv_err(@() jgraph.pcsc.connect(r0, 'Protocol', 'raw')) sprintf(' // %d', jgraph.internal.pcscsim('open'))];
        case 'sharing'
            a = jgraph.pcsc.connect;
            b = jgraph.pcsc.connect(r0);
            T = jgraph.pcsc.readers;
            s1 = T.State(1);
            third = dv_err(@() jgraph.pcsc.connect(r0, Share="exclusive"));
            clear a b
            x = jgraph.pcsc.connect(r0, Share="exclusive");
            T = jgraph.pcsc.readers;
            s2 = T.State(1);
            other = dv_err(@() jgraph.pcsc.connect(r0));
            clear x
            T = jgraph.pcsc.readers;
            out = sprintf('%s %s %s // %s // %s', s1, s2, T.State(1), third, other);
        case 'direct'
            % A direct connection reaches a reader with no card in it.
            d = jgraph.pcsc.connect(r1, Share="direct");
            s = status(d);
            e = control(d, jgraph.pcsc.ctlcode(2048), [4 5]);
            out = sprintf('%s %s [%s] %s [%s] // %s', d.Protocol, d.Share, d.ATR, s.State, sprintf('%d ', e), dv_err(@() transmit(d, getdata)));
        case 'no_reader_named'
            % With no reader named: the one that holds a card, or the refusal that says why not.
            a = jgraph.pcsc.connect;
            n1 = a.Reader;
            clear a
            several = dv_err(@() jgraph.pcsc.connect(Share="direct"));
            jgraph.internal.pcscsim('remove');
            none = dv_err(@() jgraph.pcsc.connect);
            jgraph.internal.pcscsim('unplug', r0);
            d = jgraph.pcsc.connect(Share="direct");
            n2 = d.Reader;
            clear d
            jgraph.internal.pcscsim('unplug', r1);
            T = jgraph.pcsc.readers;
            out = sprintf('%s // %s // %s // %s // %d %d // %s // %s', n1, several, none, n2, size(T), dv_err(@() jgraph.pcsc.connect), dv_err(@() jgraph.pcsc.connect(1)));
        case 'transaction'
            a = jgraph.pcsc.connect;
            b = jgraph.pcsc.connect;
            beginTransaction(a);
            t1 = a.InTransaction;
            busy = dv_err(@() transmit(b, getdata));
            busy2 = dv_err(@() beginTransaction(b));
            [~, sw] = transmit(a, getdata);
            endTransaction(a);
            [~, sw2] = transmit(b, getdata);
            out = sprintf('%d %d %s %s // %s // %s // %s // %s', t1, a.InTransaction, sw, sw2, busy, busy2, dv_err(@() endTransaction(a)), dv_err(@() beginTransaction(a, 1)));
        case 'reset'
            % A reset through one connection is seen by the other until it reconnects; it forgets the selection.
            a = jgraph.pcsc.connect;
            b = jgraph.pcsc.connect;
            transmit(a, select);
            beginTransaction(a);
            endTransaction(a, Disposition="reset");
            [~, s1] = transmit(a, getdata);
            seen = dv_err(@() transmit(b, getdata));
            reconnect(b);
            [~, s2] = transmit(b, getdata);
            transmit(b, select);
            reconnect(b, Initialization="reset", Share="shared", Protocol="T1");
            [~, s3] = transmit(b, getdata);
            out = sprintf('%s %s %s %s // %s // %s // %s', s1, s2, s3, b.Protocol, seen, dv_err(@() reconnect(b, 'Initialization', 'eject')), dv_err(@() endTransaction(a, 'Disposition', 'x')));
        case 'removed'
            c = jgraph.pcsc.connect;
            jgraph.internal.pcscsim('remove');
            gone = dv_err(@() transmit(c, getdata));
            stat = dv_err(@() status(c));
            again = dv_err(@() reconnect(c));
            jgraph.internal.pcscsim('insert', r0);
            still = dv_err(@() transmit(c, getdata));
            reconnect(c);
            [~, sw] = transmit(c, getdata);
            out = sprintf('%s // %s // %s // %s // %s %s', gone, stat, again, still, sw, c.ATR);
        case 'disconnect'
            a = jgraph.pcsc.connect;
            b = jgraph.pcsc.connect;
            n2 = jgraph.internal.pcscsim('open');
            disconnect(a, Disposition="reset");
            n1 = jgraph.internal.pcscsim('open');
            seen = dv_err(@() transmit(b, getdata));
            disconnect(b);
            out = [sprintf('%d %d %d %d %d // ', n2, n1, jgraph.internal.pcscsim('open'), isvalid(a), isvalid(b)) seen ' // ' dv_err(@() transmit(a, getdata)) ...
                ' // ' dv_err(@() a.Reader) ' // ' dv_err(@() disconnect(a)) ' // ' ix_flat(evalc('disp(a)'))];
        case 'disconnect_bad'
            a = jgraph.pcsc.connect;
            out = [dv_err(@() disconnect(a, 'Disposition', 'smash')) ' // ' dv_err(@() disconnect(a, 1)) sprintf(' // %d', isvalid(a))];
        case 'clear'
            a = jgraph.pcsc.connect;
            b = jgraph.pcsc.connect;
            beginTransaction(a);
            n2 = jgraph.internal.pcscsim('open');
            clear a
            n1 = jgraph.internal.pcscsim('open');
            % The transaction went with its connection.
            [~, sw] = transmit(b, getdata);
            delete(b);
            out = sprintf('%d %d %d %s %d', n2, n1, jgraph.internal.pcscsim('open'), sw, isvalid(b));
        case 'set_get'
            c = jgraph.pcsc.connect;
            c.UserData = 7;
            set(c, 'userdata', c.UserData + 1);
            out = sprintf('%d %s // %s // %s // %s', c.UserData, get(c, 'READER'), pcsc_set(c, 'ATR', 'x'), pcsc_set(c, 'Share', 'direct'), dv_err(@() c.NoSuch));
        case 'watch'
            w = jgraph.pcsc.watch(@pcsc_log);
            jgraph.internal.pcscsim('insert', r1);
            jgraph.internal.pcscsim('unplug', r1);
            jgraph.internal.pcscsim('plug', r1);
            before = numel(DVLOG);
            pause(0.2);
            n = w.Events;
            delete(w);
            jgraph.internal.pcscsim('insert', r0);
            pause(0.2);
            out = sprintf('%d %d %d %d // %s', before, n, numel(DVLOG), isvalid(w), strjoin(DVLOG, '; '));
        case 'watch_event'
            w = jgraph.pcsc.watch(@pcsc_evt);
            jgraph.internal.pcscsim('remove');
            pause(0.2);
            out = sprintf('%s; %s; %s // %s', class(w), strjoin(properties(w)', ' '), class(w.Callback), strjoin(DVLOG, '; '));
        case 'watch_args'
            out = [dv_err(@() jgraph.pcsc.watch()) ' // ' dv_err(@() jgraph.pcsc.watch(1)) ' // ' dv_err(@() jgraph.pcsc.watch(@pcsc_log, 1))];
        case 'watch_error'
            % A callback that throws warns, and the watch goes on.
            w = jgraph.pcsc.watch(@(src, evt) error('pcsc:boom', 'boom %s', evt.Type));
            lastwarn('');
            jgraph.internal.pcscsim('remove');
            jgraph.internal.pcscsim('insert', r1);
            warning('off', 'JGraph:pcsc:CallbackError');
            pause(0.2);
            warning('on', 'JGraph:pcsc:CallbackError');
            [msg, id] = lastwarn;
            out = sprintf('%d %s ## %s', w.Events, id, ix_flat(msg));
        otherwise
            error('pcsc_sim_case:unknown', 'unknown case %s', kind);
    end
catch e
    out = ['ERR ' e.identifier ' ' e.message];
end
clear a b c d e f w x
jgraph.internal.pcscsim('off');
out = strrep(out, '|', '/'); % a fixture value holds no |
end

function pcsc_log(~, evt)
% Logs what happened and where: "CardInserted 1 13" is a card with a 13-byte ATR into reader 1.
global DVLOG
DVLOG{end + 1} = sprintf('%s %s %d', evt.Type, evt.Reader(end), numel(strsplit(strtrim(evt.ATR))) * (strlength(evt.ATR) > 0));
end

function pcsc_evt(src, evt)
% Logs what the callback's arguments are.
global DVLOG
DVLOG{end + 1} = class(src);
DVLOG{end + 1} = class(evt);
DVLOG{end + 1} = strjoin(fieldnames(evt)', ',');
DVLOG{end + 1} = [class(evt.Type) ' ' class(evt.Reader) ' ' class(evt.ATR) ' ' class(evt.AbsTime)];
DVLOG{end + 1} = sprintf('%d', abs(seconds(datetime('now') - evt.AbsTime)) < 5);
end

function out = pcsc_set(c, name, value)
% c.(name) = value, answering 'ok' or the refusal.
try
    c.(name) = value;
    out = 'ok';
catch e
    out = [e.identifier ' ## ' ix_flat(e.message)];
end
end

function out = pcsc_outputs(c, apdu)
% Three outputs asked of transmit, which has two.
try
    [a, b, d] = transmit(c, apdu); %#ok<ASGLU>
    out = 'none';
catch e
    out = [e.identifier ' ## ' ix_flat(e.message)];
end
end
