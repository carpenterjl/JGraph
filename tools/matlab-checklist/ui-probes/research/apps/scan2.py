import os,re
root=r"C:\Program Files\MATLAB\R2025b\toolbox"
figs=0; guide=[]; cc=[]; appbase_m=[]; uicontrol_files=0; uifig_files=0; guidata_files=0
for d,_,fs in os.walk(root):
  if "\mcr\\" in d: continue
  for f in fs:
    p=os.path.join(d,f)
    if f.endswith(".fig"):
      figs+=1
      m=p[:-4]+".m"
      if os.path.exists(m):
        try:
          t=open(m,encoding="latin1").read()
          if "gui_mainfcn" in t: guide.append(m)
        except: pass
    elif f.endswith(".m"):
      try: t=open(p,encoding="latin1").read(200000)
      except: continue
      if re.search(r"classdef[^\n]*<\s*matlab\.ui\.componentcontainer\.ComponentContainer",t): cc.append(p)
      if re.search(r"classdef[^\n]*<\s*matlab\.apps\.AppBase",t): appbase_m.append(p)
      if "uicontrol(" in t: uicontrol_files+=1
      if "uifigure(" in t: uifig_files+=1
      if "guidata(" in t: guidata_files+=1
print("figs",figs,"guide pairs",len(guide)); print("\n".join(guide[:15]))
print("ComponentContainer subclasses",len(cc)); print("\n".join(cc[:15]))
print("AppBase .m",len(appbase_m)); print("\n".join(appbase_m[:10]))
print("uicontrol files",uicontrol_files,"uifigure files",uifig_files,"guidata files",guidata_files)
