function s = dv_vattr(v)
% DV_VATTR  The VISA attributes visalib.Resource drives, read back from the driver, as one line:
%   TERMCHAR_EN, TERMCHAR, ASRL_END_IN, ASRL_END_OUT, TMO_VALUE, SEND_END_EN, SUPPRESS_END_EN.
names = {'TERMCHAR_EN', 'TERMCHAR', 'ASRL_END_IN', 'ASRL_END_OUT', 'TMO_VALUE', 'SEND_END_EN', ...
    'SUPPRESS_END_EN'};
parts = cell(1, numel(names));
for k = 1:numel(names)
    try
        a = getAttributeByType(v, visalib.internal.VISAAttribute.(names{k}));
        parts{k} = sprintf('%s=%g', names{k}, a);
    catch e
        parts{k} = sprintf('%s=ERR(%s)', names{k}, e.identifier);
    end
end
s = strjoin(parts, ' ');
end
