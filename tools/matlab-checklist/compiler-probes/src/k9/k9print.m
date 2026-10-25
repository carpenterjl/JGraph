function n = k9print(n)
%K9PRINT Write n lines to standard output, a warning, and a line to standard error.
for k = 1:n
    fprintf('line %d\n', k);
end
disp(n);
warning('k9:warn', 'a warning about %d', n);
fprintf(2, 'to standard error\n');
end
