% A ':' written into the shapeless 0-by-0 takes its extent from the right side: a lone colon as many
% as the right side has over what the other subscripts select, several colons the right side's
% extents in turn. A(:, 1) = zeros(1, 5) on A = [] was refused in JGraph (it read the row count of
% the row vector) and is a 5-by-1 in R2025b.
cases = {
    'A = []; A(:, 1) = zeros(1, 5);'
    'A = []; A(:, 1) = 1:5;'
    'A = []; A(:, 1) = (1:5).'';'
    'A = []; A(1, :) = (1:5).'';'
    'A = []; A(2, :) = 1:3;'
    'A = []; A(:, 2) = 1:3;'
    'A = []; A(:, 1) = 7;'
    'A = []; A(:, [1 2]) = ones(3, 2);'
    'A = []; A(:, [1 2]) = ones(1, 3, 2);'
    'A = []; A(:, :) = 1:6;'
    'A = []; A(:, :) = (1:6).'';'
    'A = []; A(:, :) = ones(2, 3);'
    'A = []; A(:, :, 1) = 1:4;'
    'A = []; A(:, 1, :) = ones(3, 2);'
    'A = []; A(:, 1, 2) = 1:5;'
    'A = []; A(2, :, 1) = (1:3).'';'
    'A = zeros(3, 0); A(:, 1) = 1:3;'
    'A = zeros(5, 2); A(:, 1) = 1:5;'
    'A = zeros(2, 5); A(1, :) = (1:5).'';'
    'A = zeros(5, 2); A(:, 1) = ones(1, 1, 5);'
    'A = zeros(5, 3); A(:, [1 2]) = reshape(1:10, 1, 5, 2);'
    'A = int8([]); A(:, 1) = int8(1:4);'
    'A = []; A(:, 1) = [true false true];'
    };
for k = 1:numel(cases)
    try
        eval(cases{k});
        fprintf('CHK|grow_%02d|%s %s %s|exact\n', k, class(A), mat2str(size(A)), mat2str(double(A(:)).'));
    catch err
        fprintf('CHK|grow_%02d|ERR %s|exact\n', k, err.message);
    end
end

% Cells and strings take the same road.
C = {}; C(:, 1) = {1, 2, 3};
fprintf('CHK|cell_lone_colon|%s %s|exact\n', class(C), mat2str(size(C)));
C = {}; C(1, :) = {1; 2};
fprintf('CHK|cell_lone_colon_row|%s %s|exact\n', class(C), mat2str(size(C)));
S = strings(0); S(:, 1) = ["a" "b"];
fprintf('CHK|string_lone_colon|%s %s %s|exact\n', class(S), mat2str(size(S)), strjoin(S, ','));

% A target with a shape keeps it: zeros(0, 3) has no rows for the colon to fill.
try
    A = zeros(0, 3); A(:, 1) = 1:5;
    fprintf('CHK|shaped_empty|%s|exact\n', mat2str(size(A)));
catch err
    fprintf('CHK|shaped_empty|ERR %s|exact\n', err.message);
end
