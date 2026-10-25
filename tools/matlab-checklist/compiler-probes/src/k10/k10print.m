function k10print(n)
%K10PRINT Write to standard output in several shapes, a warning, and a line to standard error, for
%the C client's print and error handlers to show how the text arrives.
fprintf('line %d\n', n);
fprintf('no newline');
fprintf(' then the rest\n');
disp(n);
disp([1 2 3]);
x = n * 2 %#ok<NOPRT>
warning('k10:warn', 'a warning about %d', n);
fprintf(2, 'to standard error\n');
fprintf('two\nlines\n');
end
