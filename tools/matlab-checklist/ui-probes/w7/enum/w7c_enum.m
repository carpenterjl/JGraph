h = W7cHolder;
fprintf('size=%s class=%s isempty=%d\n', mat2str(size(h.Other)), class(h.Other), isempty(h.Other));
h.Other = 'Fast';
fprintf('then %s\n', char(h.Other));
h.Speed = "Slow";
fprintf('string gives %s\n', char(h.Speed));
try
    h.Speed = 'Nope';
catch e
    fprintf('refused: %s | %s\n', e.identifier, e.message);
end
try
    h.Speed = 3;
catch e
    fprintf('number refused: %s | %s\n', e.identifier, e.message);
end
