function record_zone_names(out)
%RECORD_ZONE_NAMES Writes R2025b's reading of every zone name it lists (open item 311, ADR 0350).
%   record_zone_names('zones.tsv') walks datetime.allTimeZones() and, for each name, records what
%   datetime(..., 'TimeZone', name) stores as TimeZone, the warning it raises (if any), and the
%   'z' letter with its daylight-saving flag on 15 January and 15 July 2026 at noon. The rows feed
%   tools/timezones/gen_zone_names.py, which writes src/JGraph.Scripting/Jgs/JgsTimeZoneNames.cs.
%
%   Run it as matlab.exe -noFigureWindows -batch "cd('<dir>'); record_zone_names('zones.tsv')".
%   R2025b reads names through its own ICU tz data (bin/icutzdata, tz 2024b) and the 'en'
%   locale's zone strings; this is the measurement of both, not a reading of either.
t = datetime.allTimeZones();
[~, ver] = timezones();
fid = fopen(out, 'w');
fprintf(fid, '# tz %s, MATLAB %s\n', ver, version);
w0 = warning('off', 'MATLAB:datetime:NonstandardTimeZone');
w1 = warning('off', 'MATLAB:datetime:NonstandardTimeZoneFixed');
for k = 1:height(t)
    name = t.Name{k};
    lastwarn('');
    try
        w = datetime(2026,1,15,12,0,0,'TimeZone',name,'Format','z');
        [~, id] = lastwarn;
        s = datetime(2026,7,15,12,0,0,'TimeZone',name,'Format','z');
        fprintf(fid, '%s\t%s\t%s\t%s\t%d\t%s\t%d\n', name, w.TimeZone, id, char(w), isdst(w), char(s), isdst(s));
    catch e
        fprintf(fid, '%s\tERR\t%s\t\t0\t\t0\n', name, e.identifier);
    end
end
warning(w0);
warning(w1);
fclose(fid);
end
