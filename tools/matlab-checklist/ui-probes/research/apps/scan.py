import zipfile,os,re,sys
root=r"C:\Program Files\MATLAB\R2025b\toolbox"
pats=["getRunningApp","runStartupFcn","varargin","ComponentContainer","uiwait","appModel.mat","Singleton","CloseRequestFcn","addlistener","timer(","matlab.apps.AppBase","uiaxes","plot("]
tot={p:0 for p in pats}; n=0; mv={}; types={}
for d,_,fs in os.walk(root):
  for f in fs:
    if f.lower().endswith(".mlapp"):
      p=os.path.join(d,f); z=zipfile.ZipFile(p); n+=1
      names=z.namelist()
      doc=z.read("matlab/document.xml").decode("utf8",'replace')
      md=z.read("metadata/appMetadata.xml").decode() if "metadata/appMetadata.xml" in names else ""
      v=re.search(r"<MLAPPVersion>(\d+)",md); t=re.search(r"<AppType>(\w+)",md)
      mv[v.group(1) if v else None]=mv.get(v.group(1) if v else None,0)+1
      types[t.group(1) if t else None]=types.get(t.group(1) if t else None,0)+1
      cls=re.search(r"classdef\s+(\w+)\s*<\s*([\w.]+)",doc)
      hits=[q for q in pats if q in doc]
      for q in hits: tot[q]+=1
      print(os.path.relpath(p,root), cls.groups() if cls else None, sorted(set(names)-{"[Content_Types].xml","_rels/.rels","matlab/document.xml","metadata/appMetadata.xml","metadata/coreProperties.xml","metadata/mwcoreProperties.xml","metadata/mwcorePropertiesExtension.xml","metadata/mwcorePropertiesReleaseInfo.xml","metadata/appScreenshot.png","appdesigner/appModel.mat"}), [q for q in ["getRunningApp","ComponentContainer","uiwait","timer("] if q in doc])
print(n, tot, mv, types)
