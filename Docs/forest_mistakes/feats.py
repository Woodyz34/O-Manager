import json, numpy as np, math
from scipy.spatial import cKDTree
from load import PK
# class -> (group, clarity, kind)  kind: 'pt' or 'ln'
PT = {'Sten':('sten',0.35),'Stor sten':('storsten',0.6),'Punkthöjd':('knoll',0.45),'Avlång punkthöjd':('knoll',0.45),
 'Liten grävd grop':('grop',0.3),'Liten naturlig grop':('grop',0.3),'Röse':('rose',0.3),'Litet röse':('rose',0.2),
 'Källa':('kalla',0.25),'Litet torn':('torn',0.6),'Högt torn':('torn',0.9),'Liten ruin':('ruin',0.55),
 'Speciellt föremål':('spec',0.4),'Särpräglat stort träd':('trad',0.35),'Grupp av stenar':('stengrupp',0.5),
 'Liten sankmark':('sank',0.3),'Brunn / fontän / reservoar':('brunn',0.4),'Vattenhål':('vattenhal',0.4),
 'Litet stenbrott/gruvhål':('stenbrott',0.5),'Foderhäck':('spec',0.4),'Speciellt terrängföremål':('spec',0.35)}
LN = {'Större väg 0.5':('vag',1.0),'Smal väg':('vag',0.9),'Körväg':('vag',0.9),'Väg: sidlinje':('vag',1.0),
 'Stor stig':('stig',0.8),'Liten stig':('lstig',0.6),'Liten otydlig stig':('ostig',0.35),'Rågång/Drivningsväg':('ragang',0.4),
 'Kraftledning':('kraft',0.9),'Större bäck':('back',0.8),'Passerbart litet vattendrag':('lback',0.5),'Mindre dike':('dike',0.4),
 'Smal sankmark, surdrag':('surdrag',0.25),'Stenmur':('mur',0.6),'Raserad stenmur':('mur',0.4),'Staket / stängsel':('staket',0.7),
 'Högt stängsel':('staket',0.9),'Beståndsgräns':('bestand',0.15),'Exakt begränsningslinje':('kant',0.45),
 'Opasserbar brant':('brant',0.75),'Passerbar brant/Skrent':('skrent',0.4),'Sjö (konturlinje)':('sjo',1.0),'Jordvall':('vall',0.4)}
HANDRAIL = 0.5   # line clarity that you can follow without compass
def name(layer): return layer.split(' ',1)[1].strip()
def load():
    pts=[]; 
    for f in json.load(open(PK+'points.json'))['features']:
        n=name(f['properties']['Layer'])
        if n in PT:
            g,c=PT[n]; x,y=f['geometry']['coordinates'][:2]; pts.append((x,y,g,c))
    lines=[]; samples=[]
    for f in json.load(open(PK+'lines.json'))['features']:
        n=name(f['properties']['Layer'])
        if n not in LN: continue
        g,c=LN[n]; cs=f['geometry']['coordinates']
        if f['geometry']['type']!='LineString' or len(cs)<2: continue
        lid=len(lines); lines.append((g,c))
        for (x0,y0,*_),(x1,y1,*_) in zip(cs[:-1],cs[1:]):
            d=math.hypot(x1-x0,y1-y0); k=max(1,int(d/5))
            for i in range(k):
                t=i/k; samples.append((x0+(x1-x0)*t,y0+(y1-y0)*t,lid))
        samples.append((cs[-1][0],cs[-1][1],lid))
    P=np.array([(p[0],p[1]) for p in pts]); Pg=[p[2] for p in pts]; Pc=np.array([p[3] for p in pts])
    S=np.array([(s[0],s[1]) for s in samples]); Sl=np.array([s[2] for s in samples])
    Sg=[lines[l][0] for l in Sl]; Sc=np.array([lines[l][1] for l in Sl])
    return dict(P=P,Pg=Pg,Pc=Pc,Pt=cKDTree(P),S=S,Sl=Sl,Sg=Sg,Sc=Sc,St=cKDTree(S),lines=lines)
