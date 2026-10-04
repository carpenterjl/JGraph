import os,zipfile,sys
H=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,H)
src=open(os.path.join(H,"build_mlapp.py")).read()
ns={"__file__":os.path.join(H,"build_mlapp.py")}; exec(src.split("code = open(SRC")[0].replace('SRC = os.path.join(HERE, "build", "ProbeApp.m.txt")',''),ns)
code="classdef Same < handle\n    properties (Constant)\n        Src = 'mlapp'\n    end\nend\n"
ns['write'](os.path.join(H,"build","dup","Same.mlapp"),[("[Content_Types].xml",ns['CT']),("_rels/.rels",ns['RELS']),("matlab/document.xml",ns['doc'](code))])
open(os.path.join(H,"build","dup","Same.m"),"w").write(code.replace("'mlapp'","'m'"))
fn="classdef Plain < handle\n    methods\n        function obj = Plain()\n            disp('Plain constructed from mlapp');\n        end\n    end\nend\n"
ns['write'](os.path.join(H,"build","dup","Plain.mlapp"),[("[Content_Types].xml",ns['CT']),("_rels/.rels",ns['RELS']),("matlab/document.xml",ns['doc'](fn))])
print("ok")
