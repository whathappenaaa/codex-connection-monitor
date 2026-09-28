"""Generate original vector/raster publication graphics. Optional dependency: Pillow.

Screenshots are copied unchanged from the app's synthetic layout test.
No real account data, real task names or external artwork is used.
"""
from pathlib import Path
from html import escape
import re
import shutil
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONT = Path('C:/Windows/Fonts/msyh.ttc')
BOLD = Path('C:/Windows/Fonts/msyhbd.ttc')

class Canvas:
    def __init__(self, width, height, background):
        self.width, self.height = width, height
        self.image = Image.new('RGB', (width, height), background)
        self.draw = ImageDraw.Draw(self.image)
        self.svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">', f'<rect width="100%" height="100%" fill="{background}"/>']
    def rect(self, x, y, w, h, color, radius=0):
        self.draw.rounded_rectangle((x,y,x+w,y+h), radius=radius, fill=color)
        self.svg.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{radius}" fill="{color}"/>')
    def circle(self, x, y, radius, color):
        self.draw.ellipse((x-radius,y-radius,x+radius,y+radius),fill=color)
        self.svg.append(f'<circle cx="{x}" cy="{y}" r="{radius}" fill="{color}"/>')
    def line(self, points, color, width):
        self.draw.line(points,fill=color,width=width,joint='curve')
        p=' '.join(f'{x},{y}' for x,y in points)
        self.svg.append(f'<polyline points="{p}" fill="none" stroke="{color}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>')
    def text(self, x, y, text, size, color, bold=False):
        font=ImageFont.truetype(str(BOLD if bold else FONT),size)
        self.draw.text((x,y),text,font=font,fill=color,anchor='ls')
        self.svg.append(f'<text x="{x}" y="{y}" font-family="Microsoft YaHei, sans-serif" font-size="{size}" font-weight="{700 if bold else 400}" fill="{color}">{escape(text)}</text>')
    def save(self, folder, name):
        folder.mkdir(parents=True,exist_ok=True)
        self.image.save(folder/f'{name}.png')
        (folder/f'{name}.svg').write_text('\n'.join(self.svg+['</svg>']),encoding='utf-8')

def icon(c,x,y,r,color,kind):
    c.circle(x,y,r,color)
    k=r/16
    def points(seq):return [(round(x+(a-16)*k),round(y+(b-16)*k)) for a,b in seq]
    w=max(2,round(2.6*k))
    if kind=='red':
        c.line(points([(10,10),(22,22)]),'#ffffff',w);c.line(points([(22,10),(10,22)]),'#ffffff',w)
    elif kind=='yellow':
        c.line(points([(16,8),(16,18)]),'#ffffff',w);c.circle(x,round(y+8*k),max(2,round(k*1.6)),'#ffffff')
    elif kind=='blue':c.line(points([(10,16),(14,20),(23,11)]),'#ffffff',w)
    elif kind=='green':c.line(points([(6,17),(11,17),(14,10),(18,23),(22,16),(26,16)]),'#ffffff',w)
    else:c.line(points([(10,16),(22,16)]),'#ffffff',w)

def main():
    c=Canvas(1920,1080,'#0b1621')
    c.rect(92,80,395,62,'#183b37',18);c.text(119,124,'免费开源  /  WINDOWS',30,'#9ce4c8',True)
    c.text(88,305,'Codex 掉线了吗？',101,'#f4f8fb',True)
    c.text(88,446,'额度还剩多少？',101,'#f4f8fb',True)
    c.text(94,555,'托盘看状态 · 窗口看额度',43,'#adc3d0')
    c.rect(94,625,998,112,'#18303d',26)
    c.text(126,696,'Codex 连接灯  1.3',53,'#9ce4c8',True)
    c.text(94,948,'B站那年松江',43,'#f4f8fb',True)
    c.text(94,1010,'独立工具 · MIT 许可证 · 状态以实际证据为准',27,'#adc3d0')
    c.rect(1205,150,610,720,'#f1f6f7',32)
    c.text(1250,238,'连接状态',40,'#1b2f36',True)
    for idx,(name,color,kind) in enumerate([('有回应','#128968','green'),('异常','#cc3d49','red'),('待确认','#a7691b','yellow')]):
        x=1300+idx*190;icon(c,x,334,49,color,kind);c.text(x-51,426,name,30,'#1b2f36',True)
    c.rect(1245,481,530,281,'#ffffff',20)
    c.text(1280,551,'账号共享额度',33,'#6a7980')
    c.text(1280,626,'剩余比例  +  重置时间',31,'#1b2f36',True)
    c.rect(1280,666,445,15,'#dde8e5',7);c.rect(1280,666,290,15,'#128968',7)
    c.text(1265,827,'功能示意 · 非实时数据',27,'#6a7980')
    c.save(ROOT/'docs/video','cover')

    s=Canvas(1200,510,'#f1f6f7');s.text(38,56,'托盘综合状态 · 颜色与符号',33,'#1b2f36',True)
    s.rect(20,260,1160,184,'#142431',16)
    states=[('红色 / 中断','#cc3d49','red'),('黄色 / 异常','#a7691b','yellow'),('绿色 / 有回应','#128968','green'),('蓝色 / 本轮结束','#3574a4','blue'),('灰色 / 未知','#718287','gray')]
    for i,(name,color,kind) in enumerate(states):
        x=127+i*235;icon(s,x,136,40,color,kind);s.text(x-90,219,name,25,'#1b2f36',True);icon(s,x,314,28,color,kind);s.text(x-90,395,name,25,'#ffffff',True)
    s.text(35,486,'状态示意；HTTP 403 和额度刷新失败不会让托盘变红。',24,'#526770')
    s.save(ROOT/'docs/images','tray-states')
    capture=ROOT/'artifacts/tests/layouts/medium-zh-100.png'
    if capture.exists():shutil.copyfile(capture,ROOT/'docs/images/overview.png')
    script=(ROOT/'docs/video/逐字稿.md').read_text(encoding='utf-8')
    lines=[]
    for line in script.splitlines():
        if not line.strip() or line.startswith('#') or line.startswith('[') or line.startswith('作者：') or line.startswith('方括号'):continue
        line=line.replace('**','')
        lines.extend(p.strip() for p in re.split(r'(?<=[。！？])',line) if p.strip())
    (ROOT/'docs/video/字幕文稿.txt').write_text('字幕时间轴请按实际配音校准。实验结果分支只保留实际发生的一项。\n\n'+'\n\n'.join(lines)+'\n',encoding='utf-8')
    print('Generated original SVG/PNG graphics, unedited synthetic screenshot and subtitle text.')

if __name__=='__main__':main()
