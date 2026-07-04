import React from "react";
import {useValue} from "cs2/api";
import {FocusBoundary} from "cs2/input";
import {Panel, Button} from "cs2/ui";
import {getModule} from "cs2/modding";
import {actions, state, isServerRunning, isClientSession, PlayerStatus} from "../state";
import {useTranslate, statusToLabel} from "../utils/localization";

const LightOpaqueTheme = getModule("game-ui/common/panel/themes/light-opaque.module.scss", "classes");
const TransitionSounds = getModule("game-ui/common/animations/transition-sounds.tsx", "panelTransitionSounds");
const StringInputField = getModule("game-ui/editor/widgets/fields/string-input-field.tsx", "StringInputField");

const PORT_MIN = 1;
const PORT_MAX = 65535;

export const JoinGameMenu = () => {
    const visible = useValue(state.joinMenuVisible);
    const type = useValue(state.playerType);
    const status = useValue(state.playerStatus);
    const ipAddress = useValue(state.joinIpAddress);
    const token = useValue(state.joinToken);
    const port = useValue(state.joinPort);
    const password = useValue(state.joinPassword);
    const username = useValue(state.username);
    const errors = useValue(state.joinErrorMessage);
    const dlDone = useValue(state.downloadDone);
    const dlRemaining = useValue(state.downloadRemaining);
    const t = useTranslate();

    if (!visible) {
        return null;
    }

    const running = isServerRunning(type);
    const joining = isClientSession(type, status);
    const enabled = !running && !joining;
    const useToken = !!token?.trim();
    const portValid = Number.isInteger(port) && port >= PORT_MIN && port <= PORT_MAX;
    const hasTarget = useToken || !!ipAddress?.trim();
    const canJoin = enabled && !!username?.trim() && hasTarget && (useToken || portValid);

    const onBack = () => {
        actions.hideJoin();
        actions.showMultiplayerMenu();
    };

    return (
        <div style={{position: "fixed", top: "50%", left: "50%", transform: "translate(-50%, -50%)", zIndex: 9999}}>
        <FocusBoundary>
            <Panel
                header={t("CS2M.UI.JoinGame", "Join Game")}
                onClose={onBack}
                theme={LightOpaqueTheme}
                transitionSounds={TransitionSounds}>
                <div className="cs2m-form-body">
                    {errors.length > 0 && (
                        <div className="cs2m-error-box">
                            <strong>{t("CS2M.UI.JoinError.Intro", "Join failed:")}</strong>
                            {errors.map((e, i) => <div key={i}>{e}</div>)}
                        </div>
                    )}

                    <div className="cs2m-field">
                        <label>{t("CS2M.UI.JoinMode.Token", "Server Token")}</label>
                        <StringInputField
                            value={token ?? ""}
                            placeholder={t("CS2M.UI.JoinMode.TokenPlaceholder", "Optional — leave blank for IP+Port")}
                            disabled={!enabled}
                            onChange={actions.setJoinToken}
                        />
                    </div>

                    <div className="cs2m-field">
                        <label>{t("CS2M.UI.IPAddress", "IP Address")}</label>
                        <StringInputField
                            value={ipAddress ?? ""}
                            disabled={!enabled || useToken}
                            onChange={actions.setJoinIpAddress}
                        />
                    </div>

                    <div className="cs2m-field">
                        <label>{t("CS2M.UI.Port", "Port")}</label>
                        <StringInputField
                            value={port?.toString() ?? ""}
                            disabled={!enabled || useToken}
                            onChange={actions.setJoinPort}
                        />
                    </div>

                    <div className="cs2m-field">
                        <label>{t("CS2M.UI.Password", "Password")}</label>
                        <StringInputField
                            value={password ?? ""}
                            disabled={!enabled}
                            onChange={actions.setJoinPassword}
                        />
                    </div>

                    <div className="cs2m-field">
                        <label>{t("CS2M.UI.Username", "Username")}</label>
                        <StringInputField
                            value={username ?? ""}
                            disabled={!enabled}
                            onChange={actions.setUsername}
                        />
                    </div>

                    {status === PlayerStatus.LOADING_MAP && (
                        <div className="cs2m-progress">
                            {t("CS2M.UI.JoinStatus[DOWNLOADING_MAP]", "Downloading map")}…
                            <progress value={dlDone} max={dlDone + dlRemaining || 1}/>
                        </div>
                    )}

                    <div className="cs2m-form-footer">
                        <Button variant="flat" onSelect={onBack}>
                            {t("CS2M.UI.Back", "Back")}
                        </Button>
                        <Button variant="primary" disabled={!canJoin} onSelect={actions.joinGame}>
                            {t("CS2M.UI.JoinGame", "Join Game")}
                        </Button>
                    </div>
                </div>
            </Panel>
        </FocusBoundary>
        </div>
    );
};
