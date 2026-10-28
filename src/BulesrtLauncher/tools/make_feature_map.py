# -*- coding: utf-8 -*-
"""Draw the Bulesrt Launcher feature map (deterministic layout, verified widths)."""
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1800, 1150
BG_TOP = (9, 14, 33)
BG_BOT = (6, 9, 20)
CARD = (20, 31, 64)
CARD_EDGE = (46, 62, 108)
ACCENT = (90, 121, 232)
ACCENT_SOFT = (58, 77, 140)
TEXT = (233, 239, 252)
TEXT2 = (167, 180, 214)
DIM = (110, 124, 160)

FONT_DIR = Path(r"C:\Windows\Fonts")
MSYH = FONT_DIR / "msyh.ttc"
MSYHBD = FONT_DIR / "msyhbd.ttc"
SEGOE = FONT_DIR / "segoeuib.ttf"

problems = []


def font(path, size):
    return ImageFont.truetype(str(path), size)


def rounded(draw, box, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def text(draw, xy, s, f, fill, max_w=None, spacing=4):
    """Draw one line, wrapping by measured width; returns bottom y."""
    x, y = xy
    if max_w is None:
        draw.text((x, y), s, font=f, fill=fill)
        return y + f.size + spacing
    for line in wrap(s, f, max_w):
        draw.text((x, y), line, font=f, fill=fill)
        y += f.size + spacing
    return y


def wrap(s, f, max_w):
    lines, cur = [], ""
    for ch in s:
        trial = cur + ch
        if f.getlength(trial) > max_w and cur:
            lines.append(cur)
            cur = ch
        else:
            cur = trial
    if cur:
        lines.append(cur)
    return lines


def main():
    img = Image.new("RGB", (W, H), BG_BOT)
    d = ImageDraw.Draw(img)

    # vertical gradient
    for y in range(H):
        t = y / (H - 1)
        d.line([(0, y), (W, y)], fill=tuple(int(BG_TOP[i] + (BG_BOT[i] - BG_TOP[i]) * t) for i in range(3)))

    # top glow
    glow = Image.new("L", (W, H), 0)
    gd = ImageDraw.Draw(glow)
    gd.ellipse([W * 0.1, -H * 0.55, W * 0.9, H * 0.55], fill=90)
    glow = glow.filter(ImageFilter.GaussianBlur(120))
    img = Image.composite(Image.new("RGB", (W, H), (60, 90, 190)), img, glow.point(lambda v: v // 4))
    d = ImageDraw.Draw(img)

    f_title = font(MSYHBD, 54)
    f_sub = font(MSYH, 22)
    f_card = font(MSYHBD, 26)
    f_item = font(MSYH, 21)
    f_small = font(MSYH, 18)
    f_tag = font(MSYHBD, 17)

    # ---------- header ----------
    d.text((88, 62), "Bulesrt Launcher", font=f_title, fill=TEXT)
    d.text((90, 136), "功能图 · v2.0.0 · Windows 10/11 x64 · WPF / .NET 8", font=f_sub, fill=TEXT2)

    # version pill
    pill_text = "数据全在 data\\，插件在 plugins\\"
    pw = f_tag.getlength(pill_text) + 36
    rounded(d, [W - 88 - pw, 74, W - 88, 74 + 42], 21, (26, 39, 78), ACCENT_SOFT, 2)
    d.text((W - 88 - pw + 18, 82), pill_text, font=f_tag, fill=TEXT2)

    # ---------- cards ----------
    cards = [
        ("\uE8B7", "项目与启动", [
            "单个添加 / 编辑 / 批量粘贴路径",
            "拖快捷方式进窗口即导入，拖到分类即归类",
            "去重：覆盖 / 跳过，保留原位置",
            "ShellExecuteEx 启动，显式工作目录",
            "管理员启动（runas）、打开所在位置",
            "启动写入 data\\launch.log，便于排查",
        ]),
        ("\uE8FD", "界面与视图", [
            "图标网格 / 列表双视图，一键切换",
            "Ctrl+K 搜索：名称 / 路径模糊匹配",
            "方向键选择，Enter 启动，Delete 删除",
            "右键菜单：编辑 / 复制路径 / 删除",
            "分类拖动排序、重命名、上移下移",
            "界面缩放 100 / 110 / 125%",
        ]),
        ("\uE790", "外观与主题", [
            "深蓝 / 浅色双主题，运行时热切换",
            "自定义背景图，或一键用内置插画",
            "GIF 动图背景，可暂停在首帧",
            "模糊 / 压暗 / 图标盒子透明 / 主页背景透明",
            "主页背景真透明：透出后面的窗口",
            "顶栏侧栏渐变遮罩，保证文字可读",
        ]),
        ("\uE768", "运行状态", [
            "每 3 秒扫一次进程表，运行中显示绿点",
            "按可见窗口计数，浏览器不会误报多开",
            "切到运行中的窗口（最小化会还原）",
            "结束运行中的实例（先确认）",
            "再开一个实例：多开",
            "可在设置里关闭该功能",
        ]),
        ("\uEA86", "插件 SDK", [
            "plugins\\<名字>\\plugin.json + dll 即时加载",
            "提供命令：Ctrl+K 可搜、可运行",
            "项目右键动作、侧栏页面（WPF 自绘）",
            "启动拦截：e.Cancel 可拦下这一次",
            "每个插件独立 AssemblyLoadContext",
            "插件异常不影响启动器，日志在 plugins.log",
        ]),
        ("\uE74E", "数据与安装", [
            "data.json 原子写入，随时可备份 / 迁移",
            "图标缓存、背景图、日志都在 data\\",
            "离线安装包：内置程序，双击即装",
            "全新安装：旧 data 先备份再清空",
            "覆盖升级不碰 data\\，只换程序",
            "单文件 exe 约 1.4 MB（需 .NET 8 运行时）",
        ]),
    ]

    margin_x = 88
    gap = 28
    col_w = (W - margin_x * 2 - gap * 2) // 3
    row_h = 318
    top = 232

    for index, (icon, title, items) in enumerate(cards):
        col = index % 3
        row = index // 3
        x = margin_x + col * (col_w + gap)
        y = top + row * (row_h + gap)
        rounded(d, [x, y, x + col_w, y + row_h], 18, CARD, CARD_EDGE, 2)

        # icon tile (Segoe MDL2 glyphs, verified to exist)
        tile = 46
        rounded(d, [x + 22, y + 22, x + 22 + tile, y + 22 + tile], 12, (28, 42, 84), ACCENT_SOFT, 2)
        icon_font = font("segmdl2.ttf", 24)
        gw = icon_font.getlength(icon)
        d.text((x + 22 + (tile - gw) / 2, y + 22 + 12), icon, font=icon_font, fill=ACCENT)

        d.text((x + 82, y + 30), title, font=f_card, fill=TEXT)

        # items
        ty = y + 92
        max_w = col_w - 44
        for item in items:
            lines = wrap(item, f_item, max_w - 18)
            if ty + len(lines) * (f_item.size + 4) > y + row_h - 14:
                problems.append(f"{title}: 内容超出卡片 ({item[:12]}…)")
                break
            d.ellipse([x + 24, ty + 9, x + 30, ty + 15], fill=ACCENT)
            yy = ty
            for line in lines:
                d.text((x + 40, yy), line, font=f_item, fill=TEXT2)
                if x + 40 + f_item.getlength(line) > x + col_w - 14:
                    problems.append(f"{title}: 文本越界 {line[:14]}…")
                yy += f_item.size + 4
            ty = yy + 10

    # ---------- architecture strip ----------
    ay = top + 2 * row_h + gap + 26
    rounded(d, [margin_x, ay, W - margin_x, ay + 168], 18, CARD, CARD_EDGE, 2)
    d.text((margin_x + 24, ay + 20), "运行结构", font=f_card, fill=TEXT)

    boxes = [
        ("BulesrtLauncher.exe", "WPF 单文件 · 自绘标题栏"),
        ("Services 层", "存储 / 启动 / 图标 / 插件 / 主题"),
        ("data\\", "data.json · 图标缓存 · 背景 · 日志"),
        ("plugins\\", "plugin.json + dll · 独立加载"),
    ]
    bx = margin_x + 24
    bw = (W - margin_x * 2 - 48 - 3 * 26) // 4
    by = ay + 62
    for i, (head, sub) in enumerate(boxes):
        x = bx + i * (bw + 26)
        rounded(d, [x, by, x + bw, by + 82], 12, (23, 34, 68), ACCENT_SOFT, 2)
        d.text((x + 16, by + 14), head, font=font(MSYHBD, 20), fill=TEXT)
        d.text((x + 16, by + 44), sub, font=f_small, fill=DIM)
        if i < len(boxes) - 1:
            ax = x + bw + 6
            d.line([(ax, by + 41), (ax + 14, by + 41)], fill=ACCENT, width=3)
            d.polygon([(ax + 14, by + 41), (ax + 7, by + 37), (ax + 7, by + 45)], fill=ACCENT)

    # footer
    d.text((margin_x, ay + 132), "仓库：RexeLauncher（命名空间）· 安装：%LOCALAPPDATA%\\Programs\\BulesrtLauncher",
           font=f_small, fill=DIM)

    out = Path(r"C:\Users\xiang\Documents\deepseek-harness\default-workspace\RexeLauncher\docs\feature-map.png")
    img.save(out)
    print("saved", out, img.size, "problems:", len(problems))
    for p in problems[:10]:
        print(" -", p)


if __name__ == "__main__":
    main()
