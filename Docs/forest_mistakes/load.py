import struct, json, numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS=None
PK='/home/user/o-manager/Assets/Resources/MapPackages/illern-2025/'
def grid():
    b=open(PK+'grid.bytes','rb').read()
    h=struct.unpack('<ii4ffii',b[:36]); minx,miny,maxx,maxy,cell,r,c=h[2:]
    g=np.frombuffer(b[36:36+r*c*4],dtype='<f4').reshape(r,c).copy()
    hh=np.frombuffer(b[37+r*c*4:37+2*r*c*4],dtype='<f4').reshape(r,c).copy()
    return g,hh,minx,miny,cell
