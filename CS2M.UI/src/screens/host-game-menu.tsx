import React from "react";
import {useValue} from "cs2/api";
import {FocusBoundary} from "cs2/input";
import {Panel, Button} from "cs2/ui";
import {getModule} from "cs2/modding";
import {actions, state, isClientSession, isServerRunning} from "../state";
import {useTranslate, compatibilityText, statusToLabel} from "../utils/localization";

const LightOpaqueTheme = getModule("game-ui/common/panel/themes/light-opaque.module.scss", "classes");
const TransitionSounds = getModule("game-ui/common/animations/transition-sounds.tsx", "panelTransitionSounds");
const StringInputField = getModule("game-ui/editor/widgets/fields/string-input-field.tsx", "StringInputField");

const PORT_MIN = 1;
const PORT_MAX = 65535;

export const HostGameMenu = () => {
    const visible = useValue(state.hostMenuVisible);
    const type = useValue(state.playerType);
    const status = useValue(state.playerStatus);
    const port = useValue(state.hostPort);
    const password = useValue(state.hostPassword);
    const username = useValue(state.username);
    const modSupport = useValue(state.modSupport);
    const t = useTranslate();

    if (!visible) {
        return null;
    }

    const running = isServerRunning(type);
    const joining = isClientSession(type, status);
    const enabled = !running && !joining;
    const portValid = Number.isInteger(port) && port >= PORT_MIN && port <= PORT_MAX;
    const hasUsername = !!username?.trim();
    const canHost = enabled && portValid && hasUsername;

    const onBack = () => {
        actions.hideHost();
        actions.showMultiplayerMenu();
    };

    return (
        <div style={{position: "fixed", top: "50%", left: "50%", transform: "translate(-50%, -50%)", zIndex: 9999}}>
        <FocusBoundary>
            <Panel
                header={t("CS2M.UI.HostGame", "Host Game")}
                onClose={onBack}
                theme={LightOpaqueTheme}
                transitionSounds={TransitionSounds}>
                <div className="cs2m-form-body">
                    <div className="cs2m-section">
                        <h3 className="cs2m-section-title">{t("CS2M.UI.Host.Session", "Session")}</h3>
                        <div className="cs2m-summary-row">
                            <span>{t("CS2M.UI.Host.Mode", "Mode")}</span>
                            <span>{t("CS2M.UI.Host.Mode.Private", "Private Host")}</span>
                        </div>
                        <div className="cs2m-summary-row">
                            <span>{t("CS2M.UI.Host.JoinToken", "Join Token")}</span>
                            <span>{t("CS2M.UI.Host.JoinToken.Pending", "Generated after start")}</span>
                        </div>
                    </div>

                    <div className="cs2m-field">
                        <label>{t("CS2M.UI.Port", "Port")}</label>
                        <StringInputField
                            value={port?.toString() ?? ""}
                            disabled={!enabled}
                            onChange={actions.setHostPort}
                        />
                    </div>

                    <div className="cs2m-field">
                        <label>{t("CS2M.UI.Password", "Password")}</label>
                        <StringInputField
                            value={password ?? ""}
                            disabled={!enabled}
                            onChange={actions.setHostPassword}
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

                    <div className="cs2m-section">
                        <h3 className="cs2m-section-title">{t("CS2M.UI.Host.Runtime", "Runtime")}</h3>
                        <div className="cs2m-summary-row">
                            <span>{t("CS2M.UI.Status", "Status")}</span>
                            <span>{statusToLabel[status] ?? status}</span>
                        </div>
                    </div>

                    {modSupport.length > 0 && (
                        <div className="cs2m-section">
                            <h3 className="cs2m-section-title">{t("CS2M.UI.Compatibility", "Compatibility")}</h3>
                            {modSupport.map((m, i) => (
                                <div key={i} className="cs2m-summary-row">
                                    <span>{m.name}</span>
                                    <span>{compatibilityText(t, m.support)}</span>
                                </div>
                            ))}
                        </div>
                    )}

                    <div className="cs2m-form-footer">
                        <Button variant="flat" onSelect={onBack}>
                            {t("CS2M.UI.Back", "Back")}
                        </Button>
                        {joining && (
                            <Button variant="flat" onSelect={actions.leaveSession}>
                                {t("CS2M.UI.LeaveSession", "Leave Session")}
                            </Button>
                        )}
                        {running ? (
                            <Button variant="primary" onSelect={actions.stopServer}>
                                {t("CS2M.UI.StopServer", "Stop Server")}
                            </Button>
                        ) : (
                            <Button variant="primary" disabled={!canHost} onSelect={actions.hostGame}>
                                {t("CS2M.UI.StartServer", "Start Server")}
                            </Button>
                        )}
                    </div>
                </div>
            </Panel>
        </FocusBoundary>
        </div>
    );
};
