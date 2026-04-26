import requests
import pandas as pd
from datetime import datetime
from io import StringIO
import sys
import io
import time
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

# ════════════════════════════════════════════════
# 欄位對應表：以上市為主
# ════════════════════════════════════════════════
TPEx_to_TWSE_mapping = {
    "代號": "證券代號",
    "名稱": "證券名稱",
    "外資及陸資(不含外資自營商)-買進股數": "外陸資買進股數(不含外資自營商)",
    "外資及陸資(不含外資自營商)-賣出股數": "外陸資賣出股數(不含外資自營商)",
    "外資及陸資(不含外資自營商)-買賣超股數": "外陸資買賣超股數(不含外資自營商)",
    "外資自營商-買進股數": "外資自營商買進股數",
    "外資自營商-賣出股數": "外資自營商賣出股數",
    "外資自營商-買賣超股數": "外資自營商買賣超股數",
    "投信-買進股數": "投信買進股數",
    "投信-賣出股數": "投信賣出股數",
    "投信-買賣超股數": "投信買賣超股數",
    "自營商(自行買賣)-買進股數": "自營商買進股數(自行買賣)",
    "自營商(自行買賣)-賣出股數": "自營商賣出股數(自行買賣)",
    "自營商(自行買賣)-買賣超股數": "自營商買賣超股數(自行買賣)",
    "自營商(避險)-買進股數": "自營商買進股數(避險)",
    "自營商(避險)-賣出股數": "自營商賣出股數(避險)",
    "自營商(避險)-買賣超股數": "自營商買賣超股數(避險)",
    "自營商-買進股數": "自營商買進股數",
    "自營商-賣出股數": "自營商賣出股數",
    "自營商-買賣超股數": "自營商買賣超股數",
    "三大法人買賣超股數合計": "三大法人買賣超股數",
}

TWSE_columns = [
    "日期","市場","證券代號","證券名稱",
    "外陸資買進股數(不含外資自營商)","外陸資賣出股數(不含外資自營商)","外陸資買賣超股數(不含外資自營商)",
    "外資自營商買進股數","外資自營商賣出股數","外資自營商買賣超股數",
    "投信買進股數","投信賣出股數","投信買賣超股數",
    "自營商買進股數","自營商賣出股數","自營商買賣超股數",
    "自營商買進股數(自行買賣)","自營商賣出股數(自行買賣)","自營商買賣超股數(自行買賣)",
    "自營商買進股數(避險)","自營商賣出股數(避險)","自營商買賣超股數(避險)",
    "三大法人買賣超股數"
]

def unify_columns(df: pd.DataFrame, market: str) -> pd.DataFrame:
    if market == "TPEx":
        df = df.rename(columns=TPEx_to_TWSE_mapping)
    # 強制補齊缺漏欄位
    for col in TWSE_columns:
        if col not in df.columns:
            df[col] = 0
    return df[TWSE_columns]

# ════════════════════════════════════════════════
# 上市（TWSE）
# ════════════════════════════════════════════════
def get_twse_institutional_data(date: str) -> pd.DataFrame:
    dt = datetime.strptime(date, "%Y%m%d")
    url = f"https://www.twse.com.tw/fund/T86?response=csv&date={date}&selectType=ALL"
    headers = {"User-Agent": "Mozilla/5.0"}
    try:
        resp = requests.get(url, headers=headers, timeout=15)
        resp.raise_for_status()
    except Exception:
        return pd.DataFrame()
    raw = resp.content.decode("big5", errors="replace")
    lines = raw.splitlines()
    header_idx = next((i for i,l in enumerate(lines) if "證券代號" in l), None)
    if header_idx is None: return pd.DataFrame()
    df = pd.read_csv(StringIO("\n".join(lines[header_idx:])), thousands=",")
    df.dropna(how="all", inplace=True)
    df.columns = [c.strip().strip('"') for c in df.columns]
    for col in df.columns[2:]:
        df[col] = pd.to_numeric(df[col].astype(str).str.replace(",","").str.strip('"'), errors="coerce").fillna(0)
    df.insert(0,"日期",dt.strftime("%Y-%m-%d"))
    df.insert(1,"市場","上市")
    return unify_columns(df,"TWSE")

# ════════════════════════════════════════════════
# 上櫃（TPEx）
# ════════════════════════════════════════════════
def get_tpex_institutional_data(date: str) -> pd.DataFrame:
    dt = datetime.strptime(date,"%Y%m%d")
    url="https://www.tpex.org.tw/www/zh-tw/insti/dailyTrade"
    params={"type":"Daily","sect":"AL","date":dt.strftime("%Y/%m/%d"),"id":"","response":"csv"}
    headers={"User-Agent":"Mozilla/5.0","Referer":"https://www.tpex.org.tw/"}
    try:
        resp=requests.get(url,params=params,headers=headers,timeout=15)
        resp.raise_for_status()
    except Exception: return pd.DataFrame()
    text=resp.content.decode("ms950",errors="replace")
    lines=text.splitlines()
    header_idx=next((i for i,l in enumerate(lines) if "代號" in l),None)
    if header_idx is None: return pd.DataFrame()
    df=pd.read_csv(StringIO("\n".join(lines[header_idx:])),thousands=",")
    df.dropna(how="all",inplace=True)
    df.columns=[c.strip() for c in df.columns]
    for col in df.columns[2:]:
        df[col]=pd.to_numeric(df[col].astype(str).str.replace(",","").str.strip(),errors="coerce").fillna(0)
    df.insert(0,"日期",dt.strftime("%Y-%m-%d"))
    df.insert(1,"市場","上櫃")
    return unify_columns(df,"TPEx")

# ════════════════════════════════════════════════
# 下載單日並儲存 CSV
# ════════════════════════════════════════════════
def fetch_and_save(date: str, out_dir: str) -> bool:
    """下載指定日期的三大法人資料並存為 CSV。
    回傳 True 表示成功，False 表示無資料（假日 / 停止交易日）。
    """
    dt = datetime.strptime(date, "%Y%m%d")
    if dt.weekday() >= 5:
        print(f"⚠️  {date} 為假日，跳過")
        return False
    df_twse = get_twse_institutional_data(date)
    df_tpex = get_tpex_institutional_data(date)
    frames = [f for f in [df_twse, df_tpex] if not f.empty]
    if not frames:
        print(f"⚠️  {date} 無任何資料")
        return False
    df_all = pd.concat(frames, ignore_index=True)
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, f"T86_ALL_{date}.csv")
    df_all.to_csv(out, index=False, encoding="utf-8-sig")
    print(f"✅ {date} 合併完成，共 {len(df_all)} 筆 → {out}")
    return True

# ════════════════════════════════════════════════
# 主程式
# ════════════════════════════════════════════════
if __name__ == "__main__":
    import sys, os
    from datetime import timedelta

    # 用法: script.py <start_YYYYMMDD> <end_YYYYMMDD> <output_dir>
    # 若未帶參數則進入互動模式
    if len(sys.argv) == 4:
        start_str, end_str, out_dir = sys.argv[1], sys.argv[2], sys.argv[3]
        start_dt = datetime.strptime(start_str, "%Y%m%d")
        end_dt   = datetime.strptime(end_str,   "%Y%m%d")
        existing = set()
        if os.path.isdir(out_dir):
            for fn in os.listdir(out_dir):
                if fn.startswith("T86_ALL_") and fn.endswith(".csv"):
                    existing.add(fn[len("T86_ALL_"):-len(".csv")])
        cur = start_dt
        while cur <= end_dt:
            date_str = cur.strftime("%Y%m%d")
            if date_str not in existing:
                fetch_and_save(date_str, out_dir)
                time.sleep(5)  # 每次抓完等 2 秒，避免太快被鎖
            else:
                print(f"⏩ {date_str} 已存在，跳過")
            cur += timedelta(days=1)
    else:
        import os
        date = input("查詢日期 (YYYYMMDD，Enter=今天): ").strip() or datetime.today().strftime("%Y%m%d")
        out_dir = input("輸出目錄 (Enter=目前目錄): ").strip() or "."
        fetch_and_save(date, out_dir)
