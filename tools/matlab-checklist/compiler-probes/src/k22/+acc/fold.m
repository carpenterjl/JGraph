function r = fold(v)
%FOLD The acceptance set's package function (packages and class folders plan, stage P6): the
%running sum of a row, reached as acc.fold from the console program acc_pkgtool.
arguments
    v (1,:) double
end
r = cumsum(v);
end
