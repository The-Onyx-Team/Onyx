#!/usr/bin/env python3
"""Renders the charts of an Onyx weekly report.

Reads the report data as JSON on stdin (written by ReportChartRenderer.cs) and writes {"charts": {key: svg}} as
JSON to stdout. Needs matplotlib >= 3.6. Charts use the Onyx light palette, because the report is shown and
printed as a cream sheet in both themes.
"""
import io
import json
import sys

import matplotlib

matplotlib.use("svg")
import matplotlib.pyplot as plt  # noqa: E402
from matplotlib.colors import LinearSegmentedColormap  # noqa: E402

PAPER = "#F6F2E9"
INK = "#111213"
MUTED = "#494C3B"
GRID = "#CBCBBF"
ACCENT = "#9D4D27"
# Onyx colours first, then muted companions that stay distinguishable on the cream paper.
SERIES = ["#9D4D27", "#C58848", "#494C3B", "#7A8B6F", "#5E3A22", "#D9B38C", "#3A5A6B", "#9F9B8A", "#B5653A",
          "#2F3130"]

plt.rcParams.update({
    "font.family": "DejaVu Sans",
    "font.size": 10,
    "text.color": INK,
    "axes.labelcolor": MUTED,
    "axes.edgecolor": GRID,
    "axes.facecolor": PAPER,
    "figure.facecolor": PAPER,
    "xtick.color": MUTED,
    "ytick.color": MUTED,
    "axes.spines.top": False,
    "axes.spines.right": False,
    "axes.grid": True,
    "grid.color": GRID,
    "grid.linewidth": 0.6,
    "axes.axisbelow": True,
    "legend.frameon": False,
    "svg.fonttype": "path",  # text as outlines: looks the same everywhere, no font needed in the browser
})


def hours_label(hours):
    minutes = int(round(hours * 60))
    if minutes < 60:
        return f"{minutes} min"
    return f"{minutes // 60} h {minutes % 60:02d}"


def svg(fig):
    buffer = io.StringIO()
    fig.savefig(buffer, format="svg", bbox_inches="tight", facecolor=PAPER)
    plt.close(fig)
    return buffer.getvalue()


def daily_chart(days, by_device):
    fig, ax = plt.subplots(figsize=(8, 3.4))
    bottom = [0.0] * len(days)
    for i, (name, values) in enumerate(by_device.items()):
        ax.bar(days, values, bottom=bottom, color=SERIES[i % len(SERIES)], width=0.62, label=name)
        bottom = [b + v for b, v in zip(bottom, values)]
    for x, total in enumerate(bottom):
        if total > 0:
            ax.annotate(hours_label(total), (x, total), ha="center", va="bottom", fontsize=8, color=MUTED,
                        xytext=(0, 3), textcoords="offset points")
    ax.set_ylabel("hours")
    ax.grid(axis="x", visible=False)
    ax.set_ylim(0, max(bottom + [0.5]) * 1.18)
    if len(by_device) > 1:
        ax.legend(loc="upper left", ncol=min(len(by_device), 4), fontsize=8)
    return svg(fig)


def apps_chart(apps):
    apps = list(reversed(apps))  # largest at the top
    names = [a["name"] for a in apps]
    fig, ax = plt.subplots(figsize=(8, 0.42 * len(apps) + 0.9))
    positions = range(len(apps))
    ax.barh([p + 0.2 for p in positions], [a["previous_hours"] for a in apps], height=0.36, color=GRID,
            label="previous week")
    ax.barh([p - 0.2 for p in positions], [a["hours"] for a in apps], height=0.36, color=ACCENT, label="this week")
    for p, a in zip(positions, apps):
        ax.annotate(hours_label(a["hours"]), (a["hours"], p - 0.2), va="center", fontsize=8, color=INK,
                    xytext=(4, 0), textcoords="offset points")
    ax.set_yticks(list(positions))
    ax.set_yticklabels(names)
    ax.set_xlabel("hours")
    ax.grid(axis="y", visible=False)
    ax.set_xlim(0, max([a["hours"] for a in apps] + [a["previous_hours"] for a in apps] + [0.5]) * 1.2)
    handles, labels = ax.get_legend_handles_labels()
    ax.legend(handles[::-1], labels[::-1], loc="lower right", fontsize=8)
    return svg(fig)


def categories_chart(categories):
    fig, ax = plt.subplots(figsize=(4.2, 4.2))
    values = [c["hours"] for c in categories]
    total = sum(values) or 1
    ax.pie(values, colors=[SERIES[i % len(SERIES)] for i in range(len(values))], startangle=90, counterclock=False,
           wedgeprops={"width": 0.38, "edgecolor": PAPER, "linewidth": 2})
    ax.text(0, 0, hours_label(total), ha="center", va="center", fontsize=13, fontweight="bold", color=INK)
    ax.legend([f'{c["name"]} ({c["hours"] / total:.0%})' for c in categories], loc="upper center",
              bbox_to_anchor=(0.5, 0.0), ncol=2, fontsize=8)
    ax.set_aspect("equal")
    return svg(fig)


def trend_chart(trend):
    labels = trend["labels"]
    fig, ax = plt.subplots(figsize=(6, 3.4))
    x = list(range(len(labels)))
    for i, (name, values) in enumerate(trend["devices"].items()):
        ax.plot(x, values, color=SERIES[(i + 1) % len(SERIES)], linewidth=1.2, alpha=0.8, label=name)
    ax.plot(x, trend["total"], color=ACCENT, linewidth=2.6, marker="o", label="all devices")
    ax.scatter([x[-1]], [trend["total"][-1]], s=110, color=ACCENT, zorder=5, edgecolor=PAPER, linewidth=2)
    ax.set_xticks(x)
    ax.set_xticklabels(labels, fontsize=8)
    ax.set_ylabel("hours per week")
    ax.set_ylim(bottom=0)
    ax.legend(loc="upper left", ncol=min(len(trend["devices"]) + 1, 4), fontsize=8)
    return svg(fig)


def heatmap_chart(days, minutes):
    cmap = LinearSegmentedColormap.from_list("onyx", [PAPER, "#C58848", ACCENT, "#3A1E0F"])
    fig, ax = plt.subplots(figsize=(8, 2.9))
    image = ax.imshow(minutes, aspect="auto", cmap=cmap, vmin=0, vmax=60, interpolation="nearest")
    ax.set_yticks(range(len(days)))
    ax.set_yticklabels(days)
    ax.set_xticks(range(0, 24, 3))
    ax.set_xticklabels([f"{h:02d}:00" for h in range(0, 24, 3)])
    ax.grid(False)
    for spine in ax.spines.values():
        spine.set_visible(False)
    bar = fig.colorbar(image, ax=ax, fraction=0.025, pad=0.02)
    bar.set_label("minutes per hour")
    bar.outline.set_visible(False)
    return svg(fig)


def device_chart(days, daily, apps):
    fig, (left, right) = plt.subplots(1, 2, figsize=(8, 2.8), gridspec_kw={"width_ratios": [1.1, 1]})
    left.bar(days, daily, color=ACCENT, width=0.62)
    left.set_ylabel("hours")
    left.grid(axis="x", visible=False)
    left.tick_params(axis="x", labelsize=8)
    apps = list(reversed(apps))
    if apps:
        right.barh([a["name"] for a in apps], [a["hours"] for a in apps], color="#C58848", height=0.55)
        for p, a in enumerate(apps):
            right.annotate(hours_label(a["hours"]), (a["hours"], p), va="center", fontsize=8,
                           xytext=(4, 0), textcoords="offset points")
        right.set_xlim(0, max(a["hours"] for a in apps) * 1.35)
    right.grid(axis="y", visible=False)
    right.tick_params(axis="y", labelsize=8)
    right.set_xlabel("hours")
    fig.tight_layout()
    return svg(fig)


def main():
    data = json.load(sys.stdin)
    days = data["days"]
    charts = {"daily": daily_chart(days, data["daily_by_device"])}
    if data["apps"]:
        charts["apps"] = apps_chart(data["apps"])
    if data["categories"]:
        charts["categories"] = categories_chart(data["categories"])
    charts["trend"] = trend_chart(data["trend"])
    charts["heatmap"] = heatmap_chart(days, data["heatmap"])
    for device in data["devices"]:
        charts[device["key"]] = device_chart(data.get("days_short") or days, device["daily"], device["apps"])
    json.dump({"charts": charts}, sys.stdout)


if __name__ == "__main__":
    main()
