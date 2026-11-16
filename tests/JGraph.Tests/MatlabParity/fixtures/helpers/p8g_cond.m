function r = p8g_cond(kind, v)
% A value read as a condition or beside && or || (open item 351; ADR 0383): 'if' and 'while' read V
% as a condition, 'and'/'or' as an operand of if V && 1 / if 0 || V, 'loop' beside && inside a
% for loop. The answer is the branch taken (or the count of passes).
switch kind
    case 'if'
        if v
            r = 1;
        else
            r = 0;
        end
    case 'while'
        r = 0;
        while v
            r = r + 1;
            if r > 2
                break;
            end
        end
    case 'and'
        if v && 1
            r = 1;
        else
            r = 0;
        end
    case 'or'
        if 0 || v
            r = 1;
        else
            r = 0;
        end
    case 'loop'
        r = 0;
        for k = 1:5
            if k > 2 && v
                r = r + k;
            end
        end
end
end
