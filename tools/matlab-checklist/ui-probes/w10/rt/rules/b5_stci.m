function b5_stci(a, b)
t1 = strcmp(upper(a), upper(b));
t2 = strcmp(lower(a), b);
t3 = strcmp(upper(a), 'ABC');
t4 = strcmp(upper(a), 'abc');
t5 = strcmpi(upper(a), b);
t6 = strcmpi(a, lower(b));
t7 = strcmp(lower(a), lower(b));
t8 = strncmp(upper(a), upper(b), 2);
t9 = isequal(upper(a), upper(b));
disp([t1 t2 t3 t4 t5 t6 t7 t8 t9])
end
