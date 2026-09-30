function s = midi_try(f)
% MIDI_TRY  What f() answers (ix_show), or "identifier ## message" of the error it throws (stage D10b).
try
    s = ix_show(f());
catch e
    s = [e.identifier ' ## ' ix_flat(e.message)];
end
end
