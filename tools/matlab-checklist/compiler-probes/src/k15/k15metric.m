function r = k15metric(kind)
%K15METRIC Emit a custom metric event, or fail the way asked for.
r = kind;
switch kind
    case 'counter', prodserver.metrics.incrementCounter('k15_calls_total', 1);
    case 'counter2', prodserver.metrics.incrementCounter("k15_calls_total", 2.5);
    case 'gauge', prodserver.metrics.setGauge('k15_level', 4);
    case 'gauge2', prodserver.metrics.setGauge('k15_level', -1.25);
    case 'gaugeint', prodserver.metrics.setGauge('k15_level', int8(7));
    case 'both', prodserver.metrics.incrementCounter('k15_both', 1); prodserver.metrics.setGauge('k15_both_gauge', 1e6);
    case 'big', prodserver.metrics.incrementCounter('k15_big', 123456789);
    case 'small', prodserver.metrics.setGauge('k15_small', 0.000012345);
    case 'badname', prodserver.metrics.incrementCounter('1bad', 1);
    case 'dash', prodserver.metrics.incrementCounter('k15-dash', 1);
    case 'emptyname', prodserver.metrics.incrementCounter('', 1);
    case 'cellname', prodserver.metrics.incrementCounter({'k15'}, 1);
    case 'zero', prodserver.metrics.incrementCounter('k15_zero', 0);
    case 'negative', prodserver.metrics.incrementCounter('k15_neg', -1);
    case 'textvalue', prodserver.metrics.incrementCounter('k15_text', '1');
    case 'vector', prodserver.metrics.setGauge('k15_vec', [1 2]);
    case 'complex', prodserver.metrics.setGauge('k15_cplx', 1 + 2i);
    case 'nan', prodserver.metrics.setGauge('k15_nan', NaN);
    case 'inf', prodserver.metrics.incrementCounter('k15_inf', Inf);
    case 'logical', prodserver.metrics.setGauge('k15_logical', true);
    case 'noargs', prodserver.metrics.incrementCounter();
    case 'onearg', prodserver.metrics.setGauge('k15_one');
    case 'threeargs', prodserver.metrics.setGauge('k15_three', 1, 2);
    case 'output', r = prodserver.metrics.setGauge('k15_out', 1);
    case 'raw', fprintf('[metric:counter] k15comp|k15_raw 3\n');
    case 'rawgauge', fprintf('[metric:gauge] other|k15_rawgauge 9\n');
    case 'rawbad', fprintf('[metric:counter] k15comp|k15_rawbad x\n');
    case 'rawnoarchive', fprintf('[metric:counter] k15_rawnoarchive 1\n');
    case 'rawspace', fprintf('  [metric:counter] k15comp|k15_rawspace 1\n');
    case 'rawmid', fprintf('text [metric:counter] k15comp|k15_rawmid 1\n');
    case 'rawdisp', disp('[metric:gauge] k15comp|k15_rawdisp 2');
    case 'rawstderr', fprintf(2, '[metric:counter] k15comp|k15_rawstderr 1\n');
    case 'rawdash', fprintf('[metric:counter] k15comp|k15-raw-dash 1\n');
    case 'rawtwo', fprintf('[metric:counter] k15comp|k15_rawtwo 1\n[metric:counter] k15comp|k15_rawtwo 1\n');
    case 'rawcounterdown', fprintf('[metric:counter] k15comp|k15_calls_total -5\n');
    case 'rawtype', fprintf('[metric:histogram] k15comp|k15_rawtype 1\n');
    case 'rawgaugeascounter', fprintf('[metric:gauge] k15comp|k15_calls_total 1\n');
    otherwise, error('k15:metric', 'Unknown kind %s.', kind);
end
end
