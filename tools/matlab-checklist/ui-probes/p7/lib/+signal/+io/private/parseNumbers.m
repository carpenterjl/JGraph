function d = parseNumbers(lines)
%PARSENUMBERS The numbers the lines hold, as a row; a line that is not a number is skipped.
%A private function of the +signal/+io folder: the members beside it reach it bare, nothing else does.
d = cellfun(@str2double, lines);
d = d(~isnan(d));
d = d(:).';
end
