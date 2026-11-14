function record_locale_names(out)
%RECORD_LOCALE_NAMES Records R2025b's month and weekday names, and its AM/PM words, per locale.
%   record_locale_names('names.tsv') writes one row per locale and field: the locale, the format
%   field (MMM, MMMM, MMMMM, eee, eeee, eeeee, a) and the texts R2025b prints for it through
%   string(t, field, locale), joined with ';'. tools/locales/gen_locale_names.py turns the table
%   into src/JGraph.Scripting/Jgs/JgsLocaleNames.cs (open item 316, ADR 0359).
%
%   The locales are R2025b's datetime.Locale suggestions and the languages near them whose
%   calendar is Gregorian and whose digits are ASCII in both engines; Arabic, Persian, Thai and
%   Hindi are left out, since .NET formats them in another calendar or R2025b in other digits.
locales = {'de_DE' 'en_GB' 'en_US' 'es_ES' 'fr_FR' 'it_IT' 'ja_JP' 'ko_KR' 'nl_NL' 'zh_CN' ...
    'pt_BR' 'pt_PT' 'ru_RU' 'sv_SE' 'pl_PL' 'fr_CA' 'fr_BE' 'fr_CH' 'de_AT' 'de_CH' 'es_MX' 'es_AR' ...
    'zh_TW' 'zh_HK' 'da_DK' 'fi_FI' 'nb_NO' 'cs_CZ' 'tr_TR' 'el_GR' 'hu_HU' 'en_AU' 'en_CA' 'en_IN' ...
    'en_IE' 'en_NZ' 'it_CH' 'nl_BE' 'ro_RO' 'sk_SK' 'uk_UA' 'bg_BG' 'hr_HR' 'sl_SI' 'id_ID' 'vi_VN' ...
    'ca_ES' 'he_IL' 'en' 'fr' 'de' 'es' 'it' 'ja' 'ko' 'nl' 'pt' 'ru' 'zh' 'sv' 'pl' 'da' 'fi' 'tr' ...
    'el' 'hu' 'cs'};
fields = {'MMM' 'MMMM' 'MMMMM' 'eee' 'eeee' 'eeeee' 'a'};
fid = fopen(out, 'w', 'n', 'UTF-8');
cleanup = onCleanup(@() fclose(fid));
fprintf(fid, '# MATLAB %s\n', version);
for li = 1:numel(locales)
    loc = locales{li};
    try
        string(datetime(2026, 1, 1), 'MMM', loc);
    catch e
        fprintf(fid, '%s\tERR\t%s\n', loc, e.identifier);  % a locale R2025b does not recognize
        continue
    end
    for fi = 1:numel(fields)
        f = fields{fi};
        switch f(1)
            case 'M'
                moments = datetime(2026, 1:12, 15);
            case 'e'
                moments = datetime(2026, 11, 1:7);  % Sunday 1 November 2026 to Saturday
            otherwise
                moments = datetime(2026, 1, 1, [6 18], 0, 0);
        end
        texts = cell(1, numel(moments));
        for k = 1:numel(moments)
            texts{k} = char(string(moments(k), f, loc));
        end
        fprintf(fid, '%s\t%s\t%s\n', loc, f, strjoin(texts, ';'));
    end
end
end
