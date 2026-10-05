function u5_rect(label, r, tol)
% One rectangle as four CHK lines, each held to within tol (U5 fixtures): a grid's 'fit' sizes come
% from text measured in a browser on one side and by GDI on the other.
names = {'x', 'y', 'w', 'h'};
for k = 1:4
    fprintf('CHK|%s_%s|%.6f|abs=%g\n', label, names{k}, r(k), tol);
end
end
