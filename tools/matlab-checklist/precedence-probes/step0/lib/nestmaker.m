function h = nestmaker()
c = 0;
h = @inc;
    function r = inc()
        c = c + 1;
        r = c;
    end
end
