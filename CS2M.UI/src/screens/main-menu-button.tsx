import {useValue, trigger} from "cs2/api";
import {getModule} from "cs2/modding";
import React from "react";
import {FloatingButton, Tooltip} from "cs2/ui";
import {FocusBoundary} from "cs2/input";
import {actions, state} from "../state";
import {useTranslate} from "../utils/localization";
import {MP_ICON} from "../icons";
import {MultiplayerHub} from "./multiplayer-hub";
import {JoinGameMenu} from "./join-game-menu";
import {HostGameMenu} from "./host-game-menu";
import styles from "./main-menu.module.scss";

/**
 * Main-menu multiplayer entry point.
 *
 * Rendered via moduleRegistry.append("GameTopLeft", ...) — same pattern used
 * by other working CS2 mods (e.g. krzychu124/Traffic). The previous
 * moduleRegistry.extend("transition-group-coordinator.tsx", ...) call
 * reliably hangs the AMD-driver / cohtml render thread ~30s after the game
 * enters the main menu, so we don't extend that file here.
 */
export const MainMenuButton = () => {
    const t = useTranslate();
    const hubVisible = useValue(state.hubMenuVisible);
    const joinVisible = useValue(state.joinMenuVisible);
    const hostVisible = useValue(state.hostMenuVisible);
    const anyMenuVisible = hubVisible || joinVisible || hostVisible;

    return (
        <>
            {!anyMenuVisible && (
                <FocusBoundary>
                    <div
                        className={styles.launcher}>
                        <Tooltip
                            tooltip={t("CS2M.UI.Multiplayer", "Multiplayer")}
                            direction="up"
                            alignment="center">
                            <FloatingButton
                                src={MP_ICON}
                                focusKey="CS2M-MainMenu-Launcher"
                                onSelect={actions.showMultiplayerMenu}
                            />
                        </Tooltip>
                    </div>
                </FocusBoundary>
            )}
            <MultiplayerHub/>
            <JoinGameMenu/>
            <HostGameMenu/>
        </>
    );
};