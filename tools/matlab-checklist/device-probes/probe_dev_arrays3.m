% probe_dev_arrays3.m -- which other device classes join (open item 36): the audio classes refuse
% concatenation; what exactly do their brackets, verbs and indexing say? Constructible headless, no
% device opened (play would need one, a constructor does not). bluetooth and visadev need hardware
% this machine lacks, so their joins stay assumed to follow serialport's (the same transport family).
p = audioplayer(zeros(4410, 1), 44100);
q = audioplayer(zeros(4410, 1), 44100);
row('ap_hcat', @() describe([p q]));
row('ap_vcat', @() describe([p; q]));
row('ap_horzcat_fn', @() describe(horzcat(p, q)));
row('ap_vertcat_fn', @() describe(vertcat(p, q)));
row('ap_repmat', @() describe(repmat(p, 1, 2)));
row('ap_cat_fn', @() describe(cat(2, p, q)));
row('ap_index_twice', @() describe(p([1 1])));
row('ap_self_join', @() describe([p p]));
row('ap_empty_join', @() describe([p []]));
r = audiorecorder;
row('ar_hcat', @() describe([r r]));
row('ar_mixed', @() describe([p r]));
row('ar_number', @() describe([r 1]));
clear

function row(name, body)
try
    out = body();
    if ~ischar(out) && ~isstring(out)
        out = class(out);
    end
catch e
    out = ['ERR ' e.identifier ' | ' e.message];
end
fprintf('%s\t%s\n', name, strrep(char(out), newline, ' / '));
end

function text = describe(v)
text = sprintf('%s %s', class(v), mat2str(size(v)));
end
