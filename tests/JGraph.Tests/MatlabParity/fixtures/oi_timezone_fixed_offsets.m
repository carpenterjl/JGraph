% Open item 209 (ADR 0344): a datetime's TimeZone given as a fixed offset is read and named as
% R2025b reads and names it - +HH:mm, or +HH:mm:ss when the seconds are not zero - through the
% constructor and the setter alike; a spelling it cannot read is MATLAB:datetime:UnknownTimeZone; a
% GMT-prefixed or aliased fixed offset is warned about; the zone format letters x, X, Z and z print
% ICU's forms for a fixed offset (and a star a letter for an unzoned value); O and zzzz are refused
% when the format is set; tzoffset is hh:mm with its daylight-saving part. Probes probe209,
% probe209b and probe209c (agent J's scratch).

zs = {'+02:00', '+02:00:00', '+0200', '+020030', '+02:00:30', '-00:00', 'Z', 'z', ...
    'utc', 'Utc', 'GMT', 'gmt', 'UT', 'ut', 'UTC+2', 'utc+2', 'UTC-05:30', 'UTC+2:00:30', 'UTC+00:00', ...
    'UT+0200', 'uT-3', 'GMT+2', 'gmt+2', 'GMT-0530', 'GMT+020', 'GMT+020030', 'GMT+00', 'gmt-0', 'GMT-14', ...
    'GMT0', 'GMT+0', 'UCT', 'Zulu', 'Etc/GMT-2', 'Etc/GMT+5', 'Etc/GMT-14', 'Etc/GMT', 'Etc/UTC', ...
    '+2', '+02', '+020', '+02000', '+0', '-0', '+000', '+123', '+12345', '+123456', '-130', '-013000', ...
    '+15:00', '+23:59', '-23:59', '+23:59:59', '-02:30:15', '-023015', '-00:00:30', '+00:59:59', ...
    '+24:00', '-24:00', '+02:60', '+02:00:60', '+2:00', '+02:0', '+02:00:0', '+02:00:00.5', '02:00', ...
    ' +02:00', '+02:00 ', ' ', '+ 2', '++2', '+-2', '+02:00:00:00', '+0200:00', '+0200000', '+99', '+096', ...
    'UTC+', 'UTC+24', 'UTC+0200', 'UTC+020', 'UTC 2', 'UTC2', 'UTC+2:0', 'GMT+24', 'GMT+2:0', 'GMT+0200:30', ...
    'GMT+', 'Z+2', 'ZZ', 'Etc/GMT+13', 'Etc/GMT-15', 'Etc/GMT-02', '+', '-'};
for k = 1:numel(zs)
    z = zs{k};
    u9b_chk(sprintf('ctor_%03d', k), @() zone_row(z, datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', z)));
end

% The setter reads the same spellings: attaching to an unzoned value keeps the wall clock, and
% converting a zoned one keeps the instant.
ss = {'+0200', '+020030', '-00:00', 'Z', 'GMT', 'gmt', 'UTC+2', 'GMT+2', 'Etc/GMT-2', '+020', '-02:30:15', ...
    '+23:59', '+24:00', '+2:00', ' +02:00', 'UTC+0200'};
for k = 1:numel(ss)
    z = ss{k};
    u9b_chk(sprintf('set_%02d', k), @() set_row(z, ''));
    u9b_chk(sprintf('setz_%02d', k), @() set_row(z, 'UTC'));
end
u9b_chk('set_string', @() set_row("+0200", ''));
u9b_chk('set_empty', @() set_row('', '+0200'));

% A duration is an offset too, written +hh:mm:ss; text or a duration and nothing else.
u9b_chk('dur_2h', @() zone_row('hours(2)', datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', hours(2))));
u9b_chk('dur_minus', @() zone_row('hours(-5.5)', datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', hours(-5.5))));
u9b_chk('dur_seconds', @() zone_row('seconds(7230)', datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', seconds(7230))));
u9b_chk('dur_fraction', @() datetime(2026, 1, 2, 'TimeZone', seconds(1.5)));
u9b_chk('dur_24h', @() datetime(2026, 1, 2, 'TimeZone', hours(24)));
u9b_chk('zone_string', @() datetime(2026, 1, 2, 'TimeZone', "+0200").TimeZone);
u9b_chk('zone_cell', @() datetime(2026, 1, 2, 'TimeZone', {'+02:00'}));
u9b_chk('zone_number', @() datetime(2026, 1, 2, 'TimeZone', 2));

% The zone letters, run by run, for fixed offsets, UTC and two named zones (whose z is a name this
% build does not have, so it is left out for them).
fz = {'+02:00', '-05:30', '+02:00:30', '-02:30:15', '+00:00', '-00:00:30', '+00:30', '+14:00', 'UTC', ...
    'America/New_York', 'Asia/Kolkata'};
fm = 'HH:mm|x|xx|xxx|xxxx|xxxxx|xxxxxx|X|XX|XXX|XXXX|XXXXX|XXXXXX|Z|ZZ|ZZZ|ZZZZ|ZZZZZ|ZZZZZZ|ZZZZZZZ';
for k = 1:numel(fz)
    u9b_chk(sprintf('fmt_%02d', k), @() fmt_row(fz{k}, fm));
end
fzz = {'+02:00', '-05:30', '+02:00:30', '-02:30:15', '+00:00', '-00:00:30', '+00:30', '+14:00', '-10:00', ...
    'UTC', 'Z', 'GMT', 'gmt', 'Etc/GMT-2'};
for k = 1:numel(fzz)
    u9b_chk(sprintf('fmt_z_%02d', k), @() fmt_row(fzz{k}, 'z|zz|zzz'));
end
u9b_chk('fmt_mixed', @() fmt_row('+02:00', 'xX zZ'));
u9b_chk('fmt_quoted', @() fmt_row('+02:00', '''x Z z O''|x'));
u9b_chk('fmt_unzoned', @() char(datetime(2026, 1, 2, 3, 4, 5, 'Format', 'HH:mm|x|xxxxx|Z|ZZZZ|ZZZZZ|z|X|')));
u9b_chk('fmt_O_option', @() datetime(2026, 1, 2, 'TimeZone', '+02:00', 'Format', 'HH:mm O'));
u9b_chk('fmt_OO_set', @() fmt_row('+02:00', 'OO'));
u9b_chk('fmt_OOOO_set', @() fmt_row('+02:00', 'HH:mm|OOOO|'));
u9b_chk('fmt_zzzz_option', @() datetime(2026, 1, 2, 'TimeZone', '+02:00', 'Format', 'HH:mm zzzz'));
u9b_chk('fmt_zzzzz_set', @() fmt_row('+02:00', 'zzzzz'));

% tzoffset: hh:mm, cut to the minute, with its daylight-saving part.
tz = {'+02:00', '-05:30', '+02:00:30', '-02:30:15', '-00:00:30', '+23:59', 'UTC', 'America/New_York', 'Asia/Kolkata'};
for k = 1:numel(tz)
    u9b_chk(sprintf('tzoffset_%d', k), @() offset_row(datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', tz{k})));
end
u9b_chk('tzoffset_summer', @() offset_row(datetime(2026, 7, 2, 'TimeZone', 'America/New_York')));
u9b_chk('tzoffset_unzoned', @() offset_row(datetime(2026, 1, 2)));
u9b_chk('isdst_fixed', @() isdst(datetime(2026, 7, 2, 'TimeZone', '+02:00')));

% Between a fixed offset and a named zone, both ways, and arithmetic across them.
u9b_chk('conv_to_named', @() conv_row('+02:00', 'America/New_York'));
u9b_chk('conv_from_named', @() conv_row('America/New_York', '+02:00'));
u9b_chk('conv_seconds_to_utc', @() conv_row('+02:00:30', 'UTC'));
u9b_chk('conv_utc_to_fixed', @() conv_row('UTC', '-05:30'));
u9b_chk('conv_same_offset', @() datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', '+0200') == datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', '+02:00'));
u9b_chk('conv_minus', @() char(datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', '+02:00') - datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', 'UTC')));
u9b_chk('conv_cat', @() cat_row('+0200', '+02:00'));
u9b_chk('conv_cat_utc', @() cat_row('+02:00', 'UTC'));
u9b_chk('conv_default_display', @() char(datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', '+02:00:30')));

function r = zone_row(given, t)
u = t;
u.TimeZone = 'UTC';
t.Format = 'yyyy-MM-dd HH:mm:ss';
u.Format = 'yyyy-MM-dd HH:mm:ss';
r = {given, t.TimeZone, char(t), char(u), char(tzoffset(t))};
end

function r = set_row(z, from)
if isempty(from)
    t = datetime(2026, 1, 2, 3, 4, 5);
else
    t = datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', from);
end
t.TimeZone = z;
t.Format = 'yyyy-MM-dd HH:mm:ss';
r = {t.TimeZone, class(t.TimeZone), char(t)};
end

function s = fmt_row(z, f)
t = datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', z);
t.Format = f;
s = char(t);
end

function r = offset_row(t)
[a, b] = tzoffset(t);
r = {char(a), char(b), a.Format, b.Format, class(a)};
end

function r = conv_row(a, b)
t = datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', a);
t.TimeZone = b;
t.Format = 'yyyy-MM-dd HH:mm:ss';
r = {t.TimeZone, char(t)};
end

function r = cat_row(a, b)
t = [datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', a) datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', b)];
t.Format = 'HH:mm:ss';
r = {t.TimeZone, char(t(1)), char(t(2))};
end
