function [int, long] = k11cwords(char, double, struct)
%K11CWORDS Arguments named as C's keywords: their sum, and their product.
int = char + double + struct;
long = char * double * struct;
end
