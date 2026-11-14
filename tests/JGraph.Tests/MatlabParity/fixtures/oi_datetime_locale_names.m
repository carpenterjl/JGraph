% Open item 316 (ADR 0359): a datetime format's month and weekday names and AM/PM words under a
% locale as R2025b prints them (its ICU data, recorded in JgsLocaleNames, where .NET's CLDR
% differs: French 'janv' not 'janv.', a Russian wide month in the genitive, narrow forms at all);
% M and e alone, MMMMMM and one-letter formats; cellstr(t, fmt) and cellstr(t, fmt, locale), which
% format as string(t, fmt, locale) does; and how a locale is spelled. Text that is not ASCII is
% written as \uXXXX.

locs = {'fr_FR', 'de_DE', 'ru_RU', 'pl_PL', 'fi_FI', 'ja_JP', 'es_MX', 'pt_BR', 'en_US', 'nb_NO', 'el_GR', 'ca_ES', 'fr'};
fields = {'MMM', 'MMMM', 'MMMMM', 'eee', 'eeee', 'eeeee', 'a'};
for li = 1:numel(locs)
    for fi = 1:numel(fields)
        u9b_chk(sprintf('n_%s_%s', locs{li}, fields{fi}), @() names(fields{fi}, locs{li}));
    end
end

% Without a locale: the narrow forms, M and e alone, and one-letter formats.
t = datetime(2026, 11, 3, 15, 4, 5);  % a Tuesday afternoon
alone = {'MMMMM', 'eeeee', 'M', 'e', 'ee', 'MMMMMM', 'd', 'h', 'H', 'm', 's', 'y', 'dd/M/uuuu', 'eee d MMM'};
for k = 1:numel(alone)
    u9b_chk(sprintf('f_%02d', k), @() esc(char(t, alone{k})));
end
u9b_chk('f_display', @() esc(char(datetime(2026, 11, 3, 'Format', 'MMMMM eeeee'))));

% Composite formats under a locale.
comp = {'dd MMMM uuuu', 'ru_RU'; 'MMMM uuuu', 'ru_RU'; 'eeee, d MMMM', 'fr_FR'; 'MMM.', 'fr_FR'; ...
    'hh:mm a', 'es_MX'; 'hh:mm a', 'ja_JP'; 'd. MMMM uuuu', 'de_DE'; 'dd-MMM-uuuu HH:mm', 'pl_PL'; ...
    'eee dd-MMM-uuuu', 'pt_BR'; 'M/d', 'fr_FR'};
for k = 1:size(comp, 1)
    u9b_chk(sprintf('c_%02d', k), @() esc(char(string(t, comp{k, 1}, comp{k, 2}))));
end

% cellstr with a format, and with a locale.
d2 = datetime(2026, 1, 15:16);
u9b_chk('cs_fmt', @() cells(cellstr(d2, 'dd-MMM-uuuu')));
u9b_chk('cs_locale', @() cells(cellstr(d2, 'dd-MMM-uuuu', 'fr_FR')));
u9b_chk('cs_col', @() cells(cellstr(d2', 'MMMM', 'de_DE')));
u9b_chk('cs_nat', @() cells(cellstr([d2 NaT], 'uuuu-MM-dd')));
u9b_chk('cs_empty_fmt', @() cells(cellstr(d2, [])));
u9b_chk('cs_empty_fmt_locale', @() cells(cellstr(d2, '', 'fr_FR')));
u9b_chk('cs_string_fmt', @() cells(cellstr(d2, "eeee")));
u9b_chk('cs_2x2', @() cells(cellstr(reshape(datetime(2026, 1:4, 1), 2, 2), 'MMM')));
u9b_chk('cs_none', @() cells(cellstr(d2(1:0), 'MMM')));
u9b_chk('cs_zone', @() cells(cellstr(datetime(2026, 1, 15, 12, 0, 0, 'TimeZone', 'UTC'), 'HH:mm z')));
u9b_chk('cs_duration', @() cells(cellstr(hours([1.5 2]), 'hh:mm')));
u9b_chk('cs_bad_fmt', @() cellstr(d2, 5));
u9b_chk('cs_bad_locale', @() cellstr(d2, 'MMM', 'french'));
u9b_chk('cs_zone_letter', @() cellstr(d2, 'O'));
u9b_chk('cs_four', @() cellstr(d2, 'MMM', 'fr_FR', 1));

% How a locale is spelled.
spell = {'fr-FR', 'FR_fr', 'fr_fr', 'en_US_POSIX', 'zh_Hans_CN', 'sr_Latn_RS', 'es_419', 'C'};
for k = 1:numel(spell)
    u9b_chk(sprintf('sp_%02d', k), @() esc(char(string(datetime(2026, 1, 15), 'MMM', spell{k}))));
end
u9b_chk('sp_french', @() string(datetime(2026, 1, 15), 'MMM', 'french'));
u9b_chk('sp_system', @() class(string(datetime(2026, 1, 15), 'MMM', 'system')));
u9b_chkdiv('sp_unknown', @() spelled('xx_YY'), 'ADR0359');
u9b_chkdiv('sp_zh', @() spelled('zh'), 'ADR0359');

function s = names(field, loc)
% The texts a field prints under a locale, months January to December, weekdays Sunday to
% Saturday, AM then PM.
switch field(1)
    case 'M'
        ts = datetime(2026, 1:12, 15);
    case 'e'
        ts = datetime(2026, 11, 1:7);
    otherwise
        ts = datetime(2026, 1, 1, [6 18], 0, 0);
end
parts = cell(1, numel(ts));
for k = 1:numel(ts)
    parts{k} = char(string(ts(k), field, loc));
end
s = esc(strjoin(parts, ';'));
end

function s = cells(c)
% A cell of text as its class, size and texts.
s = sprintf('%s %s %s', class(c), mat2str(size(c)), esc(strjoin(reshape(c, 1, []), ';')));
end

function s = spelled(loc)
% A locale's MMM, or the refusal's identifier, on one line.
try
    s = esc(char(string(datetime(2026, 1, 15), 'MMM', loc)));
catch e
    s = ['ERR ' e.identifier];
end
end

function s = esc(s)
% Text with every character past ASCII written as \uXXXX.
s = char(s);
out = cell(1, numel(s));
for k = 1:numel(s)
    if double(s(k)) > 127
        out{k} = sprintf('\\u%04X', double(s(k)));
    else
        out{k} = s(k);
    end
end
s = [out{:}];
end
