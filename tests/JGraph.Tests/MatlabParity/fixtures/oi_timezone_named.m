% Open item 311 (ADR 0350): a named time zone as R2025b matches, canonicalises and abbreviates it.
% A name is matched with its case and stored under ICU's canonical (CLDR) name: Asia/Kolkata is
% stored as Asia/Calcutta with no word said, while an alias such as US/Eastern, EST or GB is stored
% canonically with MATLAB:datetime:NonstandardTimeZone. A Windows zone id is refused. 'local' names
% the system's zone (only facts about it that hold on every machine are printed). The z letter is
% the 'en' locale's abbreviation (EST, EDT, GMT, AKST, ...) where there is one, and the offset
% written UTC+5:30 where there is not (UTC alone at zero). string(t, fmt, locale) and char(t, fmt)
% format as the Format setter would, refusing its letters. Zones whose offsets differ between
% R2025b's tz 2024b and the Windows zone data JGraph reads (Casablanca, Asuncion, Almaty, ...) are
% left out.

zs = {'America/New_York', 'America/Chicago', 'America/Denver', 'America/Phoenix', 'America/Los_Angeles', ...
    'America/Anchorage', 'Pacific/Honolulu', 'America/Adak', 'America/Halifax', 'America/St_Johns', ...
    'America/Mexico_City', 'America/Toronto', 'America/Puerto_Rico', 'America/Panama', 'America/Sao_Paulo', ...
    'America/Santiago', 'Europe/London', 'Europe/Dublin', 'Europe/Lisbon', 'Europe/Paris', 'Europe/Moscow', ...
    'Africa/Abidjan', 'Africa/Cairo', 'Africa/Johannesburg', 'Asia/Kolkata', 'Asia/Kathmandu', 'Asia/Tokyo', ...
    'Asia/Tehran', 'Australia/Sydney', 'Australia/Adelaide', 'Australia/Lord_Howe', 'Pacific/Auckland', ...
    'Pacific/Chatham', 'Atlantic/Azores', 'Atlantic/Reykjavik', 'UTC'};
for k = 1:numel(zs)
    u9b_chk(sprintf('zone_%02d', k), @() zone_row(zs{k}));
end

% The abbreviation follows the instant: New York's turns at 07:00 UTC on 8 March 2026.
u9b_chk('dst_turn', @() turn_row('America/New_York', datetime(2026, 3, 8, 6, 59, 0, 'TimeZone', 'UTC')));
u9b_chk('dst_turn_back', @() turn_row('America/New_York', datetime(2026, 11, 1, 5, 59, 0, 'TimeZone', 'UTC')));
u9b_chk('dst_london', @() turn_row('Europe/London', datetime(2026, 3, 29, 0, 59, 0, 'TimeZone', 'UTC')));

% Matched with its case; a Windows id and an unknown name are refused.
bad = {'america/new_york', 'AMERICA/NEW_YORK', 'America/new_York', 'etc/gmt-2', 'us/eastern', 'est', ...
    'Eastern Standard Time', 'Mars/Olympus', 'Asia/Kolkata ', 'Factory'};
for k = 1:numel(bad)
    u9b_chk(sprintf('bad_%02d', k), @() datetime(2026, 1, 15, 'TimeZone', bad{k}).TimeZone);
end

% Canonical names, with and without the warning, through the constructor and both setters.
links = {'US/Eastern', 'EST', 'GB', 'Japan', 'MST', 'HST', 'CET', 'US/Pacific', 'Canada/Eastern', ...
    'Asia/Kolkata', 'Europe/Kyiv', 'EST5EDT', 'PST8PDT', 'America/Argentina/Buenos_Aires', ...
    'Asia/Ho_Chi_Minh', 'America/Indiana/Indianapolis', 'Asia/Calcutta', 'Europe/Kiev', 'Navajo', 'Eire'};
for k = 1:numel(links)
    u9b_chk(sprintf('link_%02d', k), @() link_row(links{k}));
    u9b_chk(sprintf('link_set_%02d', k), @() set_row(links{k}, ''));
    u9b_chk(sprintf('link_setz_%02d', k), @() set_row(links{k}, 'UTC'));
end
u9b_chk('link_message', @() warn_text('US/Eastern'));
u9b_chk('link_message_est', @() warn_text('EST'));
u9b_chk('link_string', @() datetime(2026, 1, 15, 'TimeZone', "US/Pacific").TimeZone);

% 'local', in any case, is the system's zone by name, never the word itself.
u9b_chk('local', @() local_row('local'));
u9b_chk('local_upper', @() local_row('LOCAL'));
u9b_chk('local_same', @() strcmp(datetime(2026, 1, 15, 'TimeZone', 'local').TimeZone, datetime('now', 'TimeZone', 'local').TimeZone));
u9b_chk('local_set', @() local_set_row());

% string and char with a format, and a locale.
t = datetime(2026, 1, 15, 12, 30, 45, 'TimeZone', 'America/New_York');
u9b_chk('str_fmt', @() string(t, 'yyyy-MM-dd z'));
u9b_chk('char_fmt', @() char(t, 'yyyy-MM-dd HH:mm z'));
u9b_chk('str_fmt_x', @() string(t, 'HH:mm xxx'));
u9b_chk('str_fmt_kolkata', @() string(datetime(2026, 1, 15, 'TimeZone', 'Asia/Kolkata'), 'z'));
u9b_chk('str_fmt_O', @() string(t, 'O'));
u9b_chk('char_fmt_O', @() char(t, 'O'));
u9b_chk('str_fmt_zzzz', @() string(t, 'zzzz'));
u9b_chk('char_fmt_zzzz', @() char(t, 'zzzz'));
u9b_chk('str_fmt_loc', @() string(t, 'MMMM', 'fr_FR'));
u9b_chk('char_fmt_loc', @() char(t, 'MMMM', 'de_DE'));
u9b_chk('str_fmt_bad', @() string(t, 5));
u9b_chk('char_fmt_bad', @() char(t, 5));
u9b_chk('str_fmt_empty', @() string(t, ''));
u9b_chk('char_fmt_string', @() char(t, "dd/MM"));
u9b_chk('str_arr', @() string([t t + 1], 'dd'));
u9b_chk('str_col', @() string([t; t + 1], 'MM-dd'));
u9b_chk('str_nat', @() ismissing(string(NaT, 'dd')));
u9b_chk('str_unzoned_z', @() string(datetime(2026, 1, 15), 'HH z'));
u9b_chk('str_duration', @() string(hours(1.5), 'hh:mm'));

function r = zone_row(z)
w = datetime(2026, 1, 15, 12, 0, 0, 'TimeZone', z);
s = datetime(2026, 7, 15, 12, 0, 0, 'TimeZone', z);
wu = w;
wu.TimeZone = 'UTC';
su = s;
su.TimeZone = 'UTC';
r = {w.TimeZone, char(w, 'z|zz|zzz'), char(s, 'z|zz|zzz'), char(wu, 'HH:mm'), char(su, 'HH:mm'), isdst(w), isdst(s)};
end

function r = turn_row(z, u)
a = u;
a.TimeZone = z;
b = a + minutes(1);
r = {char(a, 'yyyy-MM-dd HH:mm z'), char(b, 'yyyy-MM-dd HH:mm z')};
end

function r = link_row(z)
t = datetime(2026, 1, 15, 12, 0, 0, 'TimeZone', z);
r = {t.TimeZone, char(t, 'HH:mm z')};
end

function r = set_row(z, from)
if isempty(from)
    t = datetime(2026, 1, 15, 12, 0, 0);
else
    t = datetime(2026, 1, 15, 12, 0, 0, 'TimeZone', from);
end
t.TimeZone = z;
r = {t.TimeZone, char(t, 'yyyy-MM-dd HH:mm')};
end

function m = warn_text(z)
w = warning('off', 'MATLAB:datetime:NonstandardTimeZone');
lastwarn('');
datetime(2026, 1, 15, 'TimeZone', z);
m = lastwarn;
warning(w);
end

function r = local_row(z)
t = datetime(2026, 1, 15, 'TimeZone', z);
n = t.TimeZone;
r = {strcmpi(n, 'local'), isempty(n), any(n == '/') || strcmp(n, 'UTC'), class(n)};
end

function r = local_set_row()
t = datetime(2026, 1, 15);
t.TimeZone = 'local';
r = {strcmp(t.TimeZone, datetime(2026, 1, 15, 'TimeZone', 'local').TimeZone), char(t, 'yyyy-MM-dd HH:mm')};
end
