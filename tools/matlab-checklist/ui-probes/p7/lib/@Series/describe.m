function r = describe(s)
%DESCRIBE One line about the series. Declared in no methods block: the file alone makes it a
%public method of Series.
r = sprintf('Series of %d, total %g', numel(s.Values), total(s));
end
