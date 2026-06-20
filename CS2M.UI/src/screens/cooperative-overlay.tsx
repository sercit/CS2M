import React, {useEffect, useRef} from "react";
import {useValue, trigger} from "cs2/api";
import {getModule} from "cs2/modding";
import {state} from "../state";
import mod from "../../mod.json";

interface RemoteCursor {
    playerId: number;
    username: string;
    x: number;
    y: number;
    z: number;
    screenX: number;
    screenY: number;
    visible: boolean;
    tool: string;
    prefab: string;
}

interface RemotePing {
    playerId: number;
    username: string;
    x: number;
    y: number;
    z: number;
    screenX: number;
    screenY: number;
    visible: boolean;
    distance: number;
    type: number;
    remaining: number;
}

interface RosterPlayer {
    playerId: number;
    username: string;
    type: string;
    latency: number;
    tool: string;
    prefab: string;
}

interface CooperativeData {
    cursors: RemoteCursor[];
    pings: RemotePing[];
    players: RosterPlayer[];
}

const LightOpaqueTheme = getModule("game-ui/common/panel/themes/light-opaque.module.scss", "classes");

const TEAM_COLORS = [
    "#5cb6ff", "#ffb45c", "#7ce892", "#d27cff",
    "#ff7c7c", "#ffe07c", "#7cd5ff", "#9eff7c",
];

function colorFor(playerId: number): string {
    if (playerId < 0) {
        return "#5cb6ff";
    }
    return TEAM_COLORS[playerId % TEAM_COLORS.length];
}

function describe(tool: string, prefab: string): string {
    if (!tool || tool === "None" || tool === "DefaultToolSystem") {
        return "Inspecting view";
    }
    if (prefab) {
        return `Building: ${prefab}`;
    }
    return `Using: ${tool.replace("ToolSystem", "")}`;
}

function playPingChime() {
    try {
        const AudioCtx = (window as any).AudioContext || (window as any).webkitAudioContext;
        if (!AudioCtx) {
            return;
        }
        const ctx = new AudioCtx();
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = "sine";
        osc.connect(gain);
        gain.connect(ctx.destination);
        const now = ctx.currentTime;
        osc.frequency.setValueAtTime(880, now);
        osc.frequency.exponentialRampToValueAtTime(1320, now + 0.12);
        gain.gain.setValueAtTime(0.18, now);
        gain.gain.exponentialRampToValueAtTime(0.001, now + 0.28);
        osc.start(now);
        osc.stop(now + 0.3);
    } catch {
        // Audio blocked or unavailable; safe to ignore.
    }
}

interface RosterProps {
    players: RosterPlayer[];
    localPlayerId: number;
}

const RosterHUD = ({players, localPlayerId}: RosterProps) => {
    if (players.length === 0) {
        return null;
    }
    return (
        <div className={`${LightOpaqueTheme.panel} cs2m-coop-roster`}>
            <div className={LightOpaqueTheme.titleBar}>
                <span className={LightOpaqueTheme.title}>Co-op lobby</span>
            </div>
            <div className={`${LightOpaqueTheme.content} cs2m-roster-list`}>
                {players.map((p) => {
                    const isLocal = p.playerId === localPlayerId;
                    const color = colorFor(p.playerId);
                    return (
                        <div key={p.playerId} className="cs2m-roster-row">
                            <span className="cs2m-roster-dot" style={{background: color}}/>
                            <span className="cs2m-roster-name">
                                {p.username}{isLocal ? " (You)" : ""}
                                <span className="cs2m-roster-status">{describe(p.tool, p.prefab)}</span>
                            </span>
                            <span className="cs2m-roster-latency">
                                {p.latency > 0 ? `${p.latency}ms` : "Host"}
                            </span>
                            {!isLocal && (
                                <button
                                    className="cs2m-roster-tp"
                                    onClick={() => trigger(mod.id, "TeleportToPlayer", p.playerId)}
                                    title={`Teleport to ${p.username}`}>
                                    ◎
                                </button>
                            )}
                        </div>
                    );
                })}
            </div>
        </div>
    );
};

export const CooperativeOverlay = () => {
    const raw = useValue(state.cooperativeData);

    let data: CooperativeData = {cursors: [], pings: [], players: []};
    try {
        if (raw) {
            const parsed = JSON.parse(raw);
            // Defensive: the C# side may write any partial snapshot, so
            // normalise every field. A single missing array crashes cohtml
            // and takes the whole HUD with it (observed on first frame after
            // entering a save, where CooperativeData = "{}").
            data = {
                cursors: Array.isArray(parsed?.cursors) ? parsed.cursors : [],
                pings: Array.isArray(parsed?.pings) ? parsed.pings : [],
                players: Array.isArray(parsed?.players) ? parsed.players : [],
            };
        }
    } catch {
        // Ignore corrupt snapshot, render empty.
    }

    const lastPingCount = useRef(0);
    useEffect(() => {
        const count = data.pings?.length ?? 0;
        if (count > lastPingCount.current) {
            playPingChime();
        }
        lastPingCount.current = count;
    }, [data.pings]);

    const localPlayerId = data.players.find(
        (p) => p.type === "SERVER" || p.playerId === 0,
    )?.playerId ?? 0;

    return (
        <div className="cs2m-coop-canvas">
            <RosterHUD players={data.players} localPlayerId={localPlayerId}/>

            {data.cursors?.map((c) =>
                c.visible ? (
                    <div
                        key={`cursor-${c.playerId}`}
                        className="cs2m-cursor"
                        style={{
                            left: `${c.screenX}%`,
                            top: `${c.screenY}%`,
                            ["--player-color" as any]: colorFor(c.playerId),
                        }}>
                        <span className="cs2m-cursor-ring"/>
                        <span className="cs2m-cursor-tag">
                            <span className="cs2m-cursor-name">{c.username}</span>
                            {(c.prefab || c.tool) && (
                                <span className="cs2m-cursor-tool">
                                    {c.prefab || c.tool.replace("ToolSystem", "")}
                                </span>
                            )}
                        </span>
                    </div>
                ) : null,
            )}

            {data.pings?.map((p, idx) =>
                p.visible ? (
                    <div
                        key={`ping-${p.playerId}-${idx}`}
                        className="cs2m-ping"
                        style={{
                            left: `${p.screenX}%`,
                            top: `${p.screenY}%`,
                            ["--player-color" as any]: colorFor(p.playerId),
                        }}>
                        <span className="cs2m-ping-pulse"/>
                        <span className="cs2m-ping-tag">
                            <span className="cs2m-ping-user">{p.username}</span>
                            <span className="cs2m-ping-dist">{Math.round(p.distance)}m</span>
                        </span>
                    </div>
                ) : null,
            )}
        </div>
    );
};
