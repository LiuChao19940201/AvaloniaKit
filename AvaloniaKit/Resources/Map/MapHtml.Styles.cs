namespace AvaloniaKit.Resources;

// ── MapHtml 分部：页面样式（CSS）──
//    主题变量见 :root（主色 --ac）；按区块分段：搜索卡/悬浮键/路线卡片/导航视图/
//    车速球/里程光柱/语音包弹层/提示与加载；末尾闭合 </head>
public static partial class MapHtml
{
    private const string StylesPart = """
<style>
  :root { --ac:#1677ff; --ink:#1a1f2b; --sub:#8a92a0; --line:#eef0f2; --bg:#f4f6f8; }
  * { margin:0; padding:0; box-sizing:border-box; -webkit-tap-highlight-color:transparent; }
  html,body { height:100%; font-family:-apple-system,"PingFang SC","Microsoft YaHei",sans-serif; color:#1a1f2b; }
  #map { position:absolute; inset:0; background:#e6e8eb; }
  button { cursor:pointer; font-family:inherit; }

  /* ── 顶部搜索卡 ── */
  .topbar { position:absolute; left:10px; right:10px; top:10px; z-index:20;
    background:#fff; border-radius:16px; padding:12px 12px 12px; box-shadow:0 6px 22px rgba(0,0,0,.16); }
  .route-row { display:flex; align-items:center; }
  .io-wrap { flex:1; min-width:0; }
  .io { display:flex; align-items:center; height:44px; }
  .io + .io { border-top:1px solid var(--line); }
  .dot { width:9px; height:9px; border-radius:50%; margin-right:10px; flex:none; }
  .dot.start { background:#12b36a; box-shadow:0 0 0 3px rgba(18,179,106,.15); }
  .dot.end { background:#f5432c; box-shadow:0 0 0 3px rgba(245,67,44,.15); }
  .io input { flex:1; min-width:0; border:none; outline:none; font-size:15px; background:transparent; color:#1a1f2b; }
  .io input::placeholder { color:#aab1bd; }
  .io-loc { flex:none; width:34px; height:34px; border:none; background:transparent;
    display:flex; align-items:center; justify-content:center; border-radius:9px; }
  .io-loc:active { background:#eaf2ff; }
  #swap { flex:none; width:36px; height:36px; margin-left:8px; border:none; background:var(--bg);
    border-radius:50%; color:#606a7b; font-size:17px; display:flex; align-items:center; justify-content:center; }
  #swap:active { background:#e6eaf0; }
  .modes { display:flex; gap:6px; margin-top:12px; }
  .modes button { flex:1; height:34px; border:none; border-radius:9px; background:var(--bg); color:#5a6270; font-size:14px; }
  .modes button.on { background:#eaf2ff; color:var(--ac); font-weight:700; }
  #go { width:100%; height:42px; margin-top:11px; border:none; border-radius:11px;
    background:var(--ac); color:#fff; font-size:15px; font-weight:700; box-shadow:0 4px 12px rgba(22,119,255,.32); }
  #go:active { background:#0f63e6; }
  /* 面板把手（短横线）：点击/滑动收起展开 */
  .grab { display:flex; justify-content:center; align-items:center; height:20px; cursor:pointer; touch-action:none; }
  .grab i { width:40px; height:4px; border-radius:2px; background:#d5dbe3; }
  .topbar.collapsed { padding-top:2px; padding-bottom:4px; }
  .topbar.collapsed .tb-body { display:none; }

  /* ── 悬浮键 ── */
  .voice-fab { position:absolute; left:12px; bottom:160px; z-index:21; height:40px; padding:0 15px;
    border:none; border-radius:20px; background:#fff; box-shadow:0 4px 14px rgba(0,0,0,.18);
    color:var(--ac); font-size:14px; font-weight:600; display:flex; align-items:center; gap:5px; }
  .loc-fab { position:absolute; right:12px; bottom:160px; z-index:21; width:44px; height:44px; border:none;
    border-radius:50%; background:#fff; box-shadow:0 4px 14px rgba(0,0,0,.2);
    display:flex; align-items:center; justify-content:center; }
  .loc-fab:active { background:#f0f4fa; }

  /* ── 多路线卡片 + 开始导航 ── */
  .plan-panel { position:absolute; left:0; right:0; bottom:0; z-index:22; background:#fff;
    border-radius:18px 18px 0 0; box-shadow:0 -4px 20px rgba(0,0,0,.15); padding:0 12px 16px; }
  .plan-panel.collapsed { padding-bottom:6px; }
  .plan-panel.collapsed .pp-body { display:none; }
  .rcards { display:flex; gap:9px; overflow-x:auto; padding:6px 0 2px; }
  .rcard { position:relative; flex:0 0 auto; min-width:126px; padding:11px 13px; border-radius:13px;
    background:var(--bg); border:1.5px solid transparent; transition:all .15s; }
  .rcard.on { background:#eaf2ff; border-color:var(--ac); box-shadow:0 3px 10px rgba(22,119,255,.22); }
  .rcard .rt { font-size:19px; font-weight:800; color:var(--ac); letter-spacing:-.2px; }
  .rcard .rd { font-size:12px; color:#6b7280; margin-top:4px; white-space:nowrap; }
  .rcard .rl { font-size:11px; color:#9aa1ad; margin-top:5px; }
  .rcard.on .rl { color:var(--ac); font-weight:600; }
  .rcard .tag { position:absolute; top:-1px; right:-1px; background:var(--ac); color:#fff;
    font-size:10px; font-weight:600; padding:2px 7px; border-radius:0 12px 0 10px; }
  .rcard .rv { font-size:11px; color:#8a92a0; margin-top:3px; max-width:160px;
    white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .start-nav { width:100%; height:46px; margin-top:12px; border:none; border-radius:12px;
    background:var(--ac); color:#fff; font-size:16px; font-weight:700; box-shadow:0 4px 12px rgba(22,119,255,.32);
    display:flex; align-items:center; justify-content:center; gap:7px; }
  .start-nav:active { background:#0f63e6; }

  /* ── 导航视图 ── */
  .nav-top { position:absolute; left:10px; right:10px; top:10px; z-index:25;
    background:linear-gradient(135deg,#1f7bff,#0e63e8); color:#fff; border-radius:16px;
    padding:13px 16px; display:flex; align-items:center; gap:15px; box-shadow:0 6px 20px rgba(14,99,232,.4); }
  .nav-top .turn-ico { font-size:40px; line-height:1; width:44px; text-align:center; flex:none; }
  .nav-top .turn-dist { font-size:14px; opacity:.92; }
  .nav-top .turn-dist b { font-size:22px; font-weight:800; margin-right:2px; }
  .nav-top .turn-instr { font-size:18px; font-weight:700; margin-top:3px;
    white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .nav-bottom { position:absolute; left:10px; right:10px; bottom:12px; z-index:25; background:#fff;
    border-radius:16px; padding:13px 14px 12px; display:flex; align-items:center;
    box-shadow:0 4px 18px rgba(0,0,0,.18); overflow:hidden; }
  .nav-prog { position:absolute; left:0; top:0; height:3px; width:100%; background:var(--line); }
  .nav-prog i { display:block; height:100%; width:0; background:var(--ac); transition:width .2s linear; }
  .nav-info { flex:1; }
  .nav-info b { font-size:19px; color:var(--ac); font-weight:800; }
  .nav-info span { display:block; font-size:12px; color:var(--sub); margin-top:3px; }
  .exit-nav { border:none; background:#fff; color:#f5432c; border:1.5px solid #f5432c; border-radius:11px;
    height:40px; padding:0 18px; font-size:15px; font-weight:600; }
  .exit-nav:active { background:#fff0ee; }
  .speed-ball { position:absolute; left:12px; bottom:96px; z-index:25; width:66px; height:66px;
    border-radius:50%; background:#fff; border:3px solid var(--ac); box-shadow:0 4px 14px rgba(0,0,0,.22);
    display:flex; flex-direction:column; align-items:center; justify-content:center; }
  .speed-ball b { font-size:22px; color:#1a1f2b; line-height:1; }
  .speed-ball span { font-size:10px; color:#8a92a0; margin-top:3px; }
  /* 右侧垂直里程光柱（底=起点 顶=终点；路况着色，走过变灰） */
  .nav-rail { position:absolute; right:10px; top:112px; bottom:110px; width:10px; z-index:24; }
  .rail-track { position:absolute; inset:0; border-radius:5px; background:#34c759;
    box-shadow:0 0 0 2px rgba(255,255,255,.95), 0 2px 8px rgba(0,0,0,.25); }
  .rail-done { position:absolute; left:0; right:0; bottom:0; height:0%; background:#b9c1cc; opacity:.92;
    border-radius:0 0 5px 5px; }
  .rail-car { position:absolute; left:50%; bottom:0%; transform:translate(-50%,50%); width:16px; height:16px;
    border-radius:50%; background:#fff; border:3px solid #1677ff; box-shadow:0 1px 5px rgba(0,0,0,.4); }

  /* ── 底部弹层（语音包） ── */
  .mask { position:absolute; inset:0; z-index:30; background:rgba(0,0,0,.4); }
  .sheet { position:absolute; left:0; right:0; bottom:0; z-index:31; background:#fff;
    border-radius:18px 18px 0 0; padding:8px 16px 18px; max-height:72%; overflow:auto; }
  .sheet::before { content:''; display:block; width:38px; height:4px; border-radius:2px;
    background:#e2e6ec; margin:8px auto 12px; }
  .sheet h3 { font-size:16px; color:#1a1f2b; margin-bottom:8px; }
  .pack { display:flex; align-items:center; justify-content:space-between; padding:14px 12px; border-radius:12px; }
  .pack.on { background:#eaf2ff; }
  .pack .pn { font-size:15px; color:#1a1f2b; font-weight:500; }
  .pack .pd { font-size:12px; color:#9aa1ad; margin-top:2px; }
  .pack .chk { color:var(--ac); font-weight:800; font-size:17px; visibility:hidden; }
  .pack.on .chk { visibility:visible; }

  /* ── 提示 / 加载 / 失败 ── */
  .toast { position:absolute; left:50%; top:42%; transform:translate(-50%,-50%); z-index:50;
    background:rgba(30,34,42,.92); color:#fff; padding:11px 18px; border-radius:12px; font-size:14px;
    opacity:0; transition:opacity .22s; pointer-events:none; max-width:78%; text-align:center; line-height:1.5;
    box-shadow:0 6px 20px rgba(0,0,0,.28); }
  .toast.show { opacity:1; }
  .spin { position:absolute; left:50%; top:42%; transform:translate(-50%,-50%); z-index:46;
    background:rgba(30,34,42,.9); color:#fff; padding:16px 22px; border-radius:14px; font-size:13px;
    display:none; flex-direction:column; align-items:center; gap:10px; }
  .spin.show { display:flex; }
  .spin-c { width:28px; height:28px; border:3px solid rgba(255,255,255,.28); border-top-color:#fff;
    border-radius:50%; animation:spin .8s linear infinite; }
  @keyframes spin { to { transform:rotate(360deg); } }

  .fail { position:absolute; inset:0; z-index:40; background:#f5f6f8; display:flex; flex-direction:column;
    align-items:center; justify-content:center; gap:14px; }
  .fail .em { font-size:48px; } .fail .tx { color:#6b7280; font-size:14px; text-align:center; padding:0 34px; line-height:1.8; }
  .hidden { display:none !important; }
</style>
</head>
""";
}
