#!/usr/bin/env python3
"""Disposable cross-version HTTP smoke: all traffic targets local CI containers."""
import json, math, os, struct, urllib.request, urllib.error, uuid, zlib

APP="http://127.0.0.1:18092"
SURVEYOR="http://127.0.0.1:18094"
TOKEN="phase16-ci-isolated-surveyor-token"
CLIENT=os.environ["PHASE16_CLIENT"]
PROVIDER=os.environ["PHASE16_PROVIDER"]

def call(root,path,method="GET",data=None,headers=None,status=200):
    request=urllib.request.Request(root+path,method=method,data=(b"" if method=="POST" and data is None else data),headers=headers or {})
    try:
        with urllib.request.urlopen(request,timeout=55) as response:
            actual,body=response.status,response.read()
    except urllib.error.HTTPError as error:
        actual,body=error.code,error.read()
    assert actual==status or (status==200 and method=="POST" and actual==201),(path,actual,status,body[:1200])
    return body

def jcall(root,path,**kwargs):
    return json.loads(call(root,path,**kwargs))

def png(pixels,width,height):
    rows=b"".join(b"\0"+bytes(pixels[y*width:(y+1)*width]) for y in range(height))
    def chunk(t,b):
        return struct.pack(">I",len(b))+t+b+struct.pack(">I",zlib.crc32(t+b)&0xffffffff)
    return (b"\x89PNG\r\n\x1a\n"
        +chunk(b"IHDR",struct.pack(">IIBBBBB",width,height,8,0,0,0,0))
        +chunk(b"IDAT",zlib.compress(rows,6))+chunk(b"IEND",b""))

def hex_image():
    width,height=420,320
    pixels=bytearray([224])*(width*height)
    spacing=30*math.sqrt(3)
    radians=-4*math.pi/180
    u=(spacing*math.cos(radians),spacing*math.sin(radians))
    v=(spacing*math.cos(radians+math.pi/3),spacing*math.sin(radians+math.pi/3))
    radius=spacing/math.sqrt(3)
    reach=math.ceil(math.hypot(width,height)/spacing)+4
    for i in range(-reach,reach+1):
        for j in range(-reach,reach+1):
            cx=11+i*u[0]+j*v[0]
            cy=9+i*u[1]+j*v[1]
            if cx< -spacing or cy< -spacing or cx>width+spacing or cy>height+spacing: continue
            points=[(cx+radius*math.cos((-34+60*k)*math.pi/180),
                     cy+radius*math.sin((-34+60*k)*math.pi/180)) for k in range(6)]
            for k in range(6):
                x0,y0=points[k];x1,y1=points[(k+1)%6]
                steps=max(1,math.ceil(math.hypot(x1-x0,y1-y0)*1.5))
                for t in range(steps+1):
                    x=x0+(x1-x0)*t/steps;y=y0+(y1-y0)*t/steps
                    for dx,dy in [(0,0),(-1,0),(1,0),(0,-1),(0,1)]:
                        xx,yy=round(x+dx),round(y+dy)
                        if 0<=xx<width and 0<=yy<height: pixels[yy*width+xx]=45
    return png(pixels,width,height)

def square_image():
    width=height=640
    pixels=bytearray([255])*(width*height)
    for y in range(height):
        for x in range(width):
            if min((x-39)%80,(39-x)%80,(y-51)%80,(51-y)%80)<=1:
                pixels[y*width+x]=0
    return png(pixels,width,height)

def polygon_raster(polygons,basis,width=640,height=640,phase=(31,53),rotation=0,scale=1):
    """Same independent pixel geometry as Surveyor's frozen raster qualifications."""
    pixels=bytearray([255])*(width*height)
    angle=math.radians(rotation)
    c,s=math.cos(angle)*scale,math.sin(angle)*scale
    def rnd(v): return math.floor(v+0.5)  # JS Math.round used by the qualification generator
    def transform(p):
        x,y=p
        return (rnd(phase[0]+x*c-y*s),rnd(phase[1]+x*s+y*c))
    def draw(a,b):
        dx,dy=b[0]-a[0],b[1]-a[1]
        steps=max(1,math.ceil(max(abs(dx),abs(dy))*2))
        for i in range(steps+1):
            x=rnd(a[0]+dx*i/steps)
            y=rnd(a[1]+dy*i/steps)
            for oy in range(-1,2):
                for ox in range(-1,2):
                    xx,yy=x+ox,y+oy
                    if 0<=xx<width and 0<=yy<height:
                        pixels[yy*width+xx]=0
    for u in range(-14,15):
        for v in range(-14,15):
            delta=(basis[0][0]*u+basis[1][0]*v,
                   basis[0][1]*u+basis[1][1]*v)
            for polygon in polygons:
                points=[transform((x+delta[0],y+delta[1])) for x,y in polygon]
                for j in range(len(points)):
                    draw(points[j],points[(j+1)%len(points)])
    return png(pixels,width,height)

def triangle_image():
    r=math.sqrt(3)/2
    return polygon_raster(
        [[(0,0),(80,0),(120,80*r)],[(0,0),(120,80*r),(40,80*r)]],
        [(80,0),(40,80*r)],phase=(39,51))

def mixed_image(seed=1907,rotation=0,scale=1):
    """Generate held-out polygon classes without giving their identity to Surveyor."""
    state=seed
    def random():
        nonlocal state
        state=(state+0x6D2B79F5)&0xffffffff
        t=state
        t=((t^(t>>15))*(1|t))&0xffffffff
        t=(t^((t+(((t^(t>>7))*(61|t))&0xffffffff))&0xffffffff))&0xffffffff
        return ((t^(t>>14))&0xffffffff)/4294967296
    modes=[int(random()*3) for _ in range(6)]
    modes[:3]=[0,1,2]
    polygons=[]
    for row in range(2):
        for col in range(3):
            x,y=48*col,48*row
            a,b,c,d=(x,y),(x+48,y),(x+48,y+48),(x,y+48)
            mode=modes[3*row+col]
            if mode==0: polygons.append([a,b,c,d])
            elif mode==1: polygons.extend([[a,b,c],[a,c,d]])
            else: polygons.extend([[a,b,d],[b,c,d]])
    return polygon_raster(polygons,[(144,0),(0,96)],
                          rotation=rotation,scale=scale)

def multipart(fields,name,image):
    boundary="phase16-"+uuid.uuid4().hex
    body=bytearray()
    for k,v in fields.items():
        body.extend(f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode())
    body.extend(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}.png"\r\nContent-Type: image/png\r\n\r\n'.encode())
    body.extend(image)
    body.extend(f"\r\n--{boundary}--\r\n".encode())
    return bytes(body),{"Content-Type":"multipart/form-data; boundary="+boundary}

def create_map(world_id,version,name,image):
    body,headers=multipart({"name":name,"geographyKey":"Phase 16 CI geography",
        "role":"Neutral","containsBakedGrid":"true","expectedVersion":str(version)},name,image)
    world=jcall(APP,f"/api/overworlds/{world_id}/source-maps",method="POST",data=body,headers=headers)
    cells=[m for m in world["sourceMaps"] if m["name"]==name]
    assert len(cells)==1,world
    mid=cells[0]["id"]
    assert call(APP,f"/api/overworlds/{world_id}/source-maps/{mid}/asset")==image
    return mid,world["version"]

def assert_saved(world_id,version,map_id,image,sid):
    world=jcall(APP,f"/api/overworlds/{world_id}")
    assert world["version"]==version,world
    assert any(m["id"]==map_id and m["alignment"] is None for m in world["sourceMaps"]),world
    assert call(APP,f"/api/overworlds/{world_id}/source-maps/{map_id}/asset")==image
    session=jcall(APP,f"/api/expeditions/{sid}")
    assert session["procedure"]["key"]=="simple-fixed-distance",session
    assert session["expedition"]["activeWatchNumber"]==1,session

def main():
    assert CLIENT in ("old","new") and PROVIDER in ("old","new")
    print(f"PHASE16_MATRIX starting {CLIENT} Hex Crawl + {PROVIDER} Surveyor",flush=True)
    capabilities={v["id"] for v in jcall(SURVEYOR,"/")["capabilities"]}
    assert "map.periodic-tiling.detect" in capabilities
    assert ("map.periodic-tiling.investigate" in capabilities)==(PROVIDER=="new")

    image=hex_image()
    observed=jcall(SURVEYOR,
        "/v2/periodic-tiling/detect?expectedDsSymbol=%3C1%3A1%2C1%2C1%3A6%2C3%3E&minimumConfidence=0.18",
        method="POST",data=image,
        headers={"Authorization":"Bearer "+TOKEN,"Content-Type":"image/png"})
    assert observed["apiVersion"]=="v2" and observed["capability"]=="map.periodic-tiling.detect",observed
    assert observed["status"]=="detected" and observed["tiling"]["dsSymbol"]=="<1:1,1,1:6,3>",observed
    call(SURVEYOR,"/v2/periodic-tiling/detect",method="POST",data=image,
        headers={"Content-Type":"image/png"},status=401)

    world=jcall(APP,"/api/overworlds",method="POST",
        data=json.dumps({"name":"Phase 16 isolated compatibility","orientation":"PointyTop",
            "origin":{"x":0,"y":0},"rotationDegrees":0,"hexRadiusWorldUnits":1,
            "neighborCenterDistance":12,
            "distanceUnit":{"kind":"Mile","symbol":"mi","metersPerUnit":1609.344}}).encode(),
        headers={"Content-Type":"application/json"})
    wid=world["id"]
    mid,version=create_map(wid,world["version"],"phase16-hex",image)
    v2=jcall(APP,f"/api/overworlds/{wid}/source-maps/{mid}/grid-analysis",method="POST")
    assert v2["apiVersion"]=="v2" and v2["capability"]=="map.periodic-tiling.detect",v2
    assert v2["status"]=="detected" and v2["tilingDsSymbol"]=="<1:1,1,1:6,3>",v2
    assert jcall(APP,f"/api/overworlds/{wid}")["version"]==version

    session=jcall(APP,f"/api/overworlds/{wid}/expeditions",method="POST",
        data=json.dumps({"name":"CI retained expedition","procedureKey":"simple-fixed-distance",
            "presentationKey":"exploration-map","startHex":{"q":0,"r":0}}).encode(),
        headers={"Content-Type":"application/json"})
    sid=session["id"]
    jcall(APP,f"/api/expeditions/{sid}/advance",method="POST",
        data=json.dumps({"expectedVersion":session["version"],"intendedDirection":0,
            "expectedDistance":12,"actualDistance":12,"resolutionSource":"ManualRoll",
            "continueAcrossBoundaries":False}).encode(),
        headers={"Content-Type":"application/json"})

    preview_status="not offered"
    if CLIENT=="new":
        investigation_mid=mid
        if PROVIDER=="new":
            investigation_mid,version=create_map(wid,version,"phase16-square",square_image())
        preview=jcall(APP,f"/api/overworlds/{wid}/source-maps/{investigation_mid}/motif-investigation",method="POST")
        preview_status=preview["status"]
        assert preview["authoritative"] is False,preview
        if PROVIDER=="old":
            assert preview_status=="unsupported" and preview["candidate"] is None,preview
        else:
            assert preview_status=="consistent-candidate" and preview["candidate"]["motifCells"],preview
            assert preview["candidate"]["dsSymbol"],preview
        assert jcall(APP,f"/api/overworlds/{wid}")["version"]==version

    assert_saved(wid,version,mid,image,sid)
    with open("/tmp/phase16-ci-state.json","w") as f:
        json.dump({"world_id":wid,"world_version":version,"map_id":mid,"expedition_id":sid},f)
    with open("/tmp/phase16-ci-hex.png","wb") as f: f.write(image)
    print(f"PHASE16_MATRIX PASS {CLIENT}/{PROVIDER} v2={v2['status']} v3={preview_status}",flush=True)

if __name__=="__main__":
    main()
