import React from "react";
import {useValue} from "cs2/api";
import {FocusBoundary} from "cs2/input";
import {Panel, Button} from "cs2/ui";
import {getModule} from "cs2/modding";
import {actions, state, isServerRunning, isClientSession, sessionActive, PlayerStatus} from "../state";
import {useTranslate, statusToLabel} from "../utils/localization";

const LightOpaqueTheme = getModule("game-ui/common/panel/themes/light-opaque.module.scss", "classes");
const TransitionSounds = getModule("game-ui/common/animations/transition-sounds.tsx", "panelTransitionSounds");

export const MultiplayerHub = () => {
    const visible = useValue(state.hubMenuVisible);
    const type = useValue(state.playerType);
    const status = useValue(state.playerStatus);
    const t = useTranslate();

    if (!visible) {
        return null;
    }

    const running = isServerRunning(type);
    const joining = isClientSession(type, status);
    const active = sessionActive(type, status);
    const canJoin = !running && !joining;

    const onJoin = () => {
        actions.hideHub();
        actions.showJoinMenu();
    };

    const onHost = () => {
        actions.hideHub();
        actions.showHostMenu();
    };

    const onLeave = () => {
        actions.leaveSession();
        actions.hideHub();
    };

    return (
        <div style={{position: "fixed", top: "50%", left: "50%", transform: "translate(-50%, -50%)", zIndex: 9999}}>
        <FocusBoundary>
            <Panel
                header={t("CS2M.UI.MultiplayerHub", "Multiplayer")}
                onClose={actions.hideHub}
                theme={LightOpaqueTheme}
                transitionSounds={TransitionSounds}>
                <div className="cs2m-hub-body">
                    <p className="cs2m-hub-status">
                        {t("CS2M.UI.CurrentStatus", "Current status")}: <strong>{statusToLabel[status] ?? status}</strong>
                    </p>

                    <div className="cs2m-hub-actions">
                        <Button variant="primary" disabled={!canJoin} onSelect={onJoin}>
                            {t("CS2M.UI.JoinGame", "Join Game")}
                        </Button>
                        <Button variant="primary" disabled={!canJoin} onSelect={onHost}>
                            {t("CS2M.UI.HostGame", "Host Game")}
                        </Button>
                    </div>

                    {running && (
                        <p className="cs2m-hub-hint">
                            {t("CS2M.UI.Hub.ServerRunningHint", "Server session active. Stop hosting before switching to Join.")}
                        </p>
                    )}
                    {joining && (
                        <p className="cs2m-hub-hint">
                            {t("CS2M.UI.Hub.ClientRunningHint", "Client session active. Leave the current server before hosting.")}
                        </p>
                    )}

                    {active && (
                        <div className="cs2m-hub-footer">
                            <Button variant="flat" onSelect={onLeave}>
                                {t("CS2M.UI.LeaveSession", "Leave Session")}
                            </Button>
                        </div>
                    )}
                </div>
            </Panel>
        </FocusBoundary>
        </div>
    );
};
