function probe_k0_numeric()
%PROBE_K0_NUMERIC Which argument strings mcc -n accepts: mainFuncForNumericStandaloneApplication
%evaluates an argument only when the builtin mclisnumerictype says it is numeric. Run under
%matlab -batch (not deployed); one line per string: the string, then 1/0 or ERR.
beep('off');
cases = {'1', '2.5', '-3', '+4', '1e3', '1E-3', '.5', '5.', 'Inf', '-inf', 'NaN', 'nan', 'pi', ...
    'true', 'false', '[1 2]', '[1,2;3 4]', '[]', '1+2i', '3i', 'j', '0x1F', '0b101', '1_000', ...
    ' 7', '7 ', '1 2', '(1)', '-[1 2]', 'int8(4)', '''txt''', 'hello', '', '1,2', '--1', '1e', 'e3'};
% Every case is char, as a deployed main's arguments are. A string scalar crashes R2025b inside
% mclisnumerictype (heap corruption, 0xc0000374, seen 2026-10-21 with "'txt'"): never pass one.
for k = 1:numel(cases)
    s = cases{k};
    try
        tf = mclisnumerictype(s);
        if tf
            v = eval(s);
            fprintf('[%s]\t1\t%s %s\n', s, class(v), mat2str(v));
        else
            fprintf('[%s]\t0\n', s);
        end
    catch ME
        fprintf('[%s]\tERR %s | %s\n', s, ME.identifier, strrep(ME.message, newline, ' / '));
    end
end
end
