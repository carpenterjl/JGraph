% A MATLAB script run from JGS whose built-in calls mean what they mean in MATLAB (V11): find is
% 1-based and find(m, 1) is a result limit; unique's second output is 1-based; sprintf cycles its
% format over an array and decodes its escapes; fprintf's \n is a newline, which is what makes each
% line below its own line.
m = [0 1 1];
k = find(m);
k1 = find(m, 1);
[u, ia] = unique([3 1 3]);
fprintf('CHK|dc_run_find|%s|exact\n', mat2str(k));
fprintf('CHK|dc_run_find_limit|%s|exact\n', mat2str(k1));
fprintf('CHK|dc_run_unique|%s|exact\n', mat2str(u));
fprintf('CHK|dc_run_unique_ia|%s|exact\n', mat2str(ia));
fprintf('CHK|dc_run_sprintf_cycles|%s|exact\n', sprintf('%d,', [1 2 3]));
fprintf('CHK|dc_run_sprintf_escape|%d|exact\n', double(sprintf('\t')));
