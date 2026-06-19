import {ModuleRegistryExtend} from "cs2/modding";
import React from "react";
import {useValue} from "cs2/api";
import {actions, state} from "../state";
import {FloatingButton, Tooltip} from "cs2/ui";
import {FocusBoundary} from "cs2/input";
import {useTranslate} from "../utils/localization";
import {MP_ICON} from "../icons";

export const MenuUIExtensions: ModuleRegistryExtend = (Component) => {
    return (props) => {
        const {children, ...otherProps} = props || {};
        const hubVisible = useValue(state.hubMenuVisible);
        const joinVisible = useValue(state.joinMenuVisible);
        const hostVisible = useValue(state.hostMenuVisible);
        const anyMenuVisible = hubVisible || joinVisible || hostVisible;
        const t = useTranslate();

        const launcher = (
            <FocusBoundary>
                <div
                    style={{
                        position: "fixed",
                        right: "44rem",
                        bottom: "44rem",
                        zIndex: 1000,
                    }}>
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
        );

        return (
            <Component {...otherProps}>
                {children}
                {!anyMenuVisible && launcher}
            </Component>
        );
    };
};