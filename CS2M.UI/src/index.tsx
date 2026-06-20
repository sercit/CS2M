import "./styles/cs2m.scss";
import {getModule, ModRegistrar} from "cs2/modding";
import {ChatIcon, ChatPanel} from "./screens/chat";
import {CooperativeOverlay} from "./screens/cooperative-overlay";
import {MainMenuButton} from "./screens/main-menu-button";

const register: ModRegistrar = (moduleRegistry) => {
    // NB: Do NOT call moduleRegistry.extend("game-ui/common/animations/
    // transition-group-coordinator.tsx", ...) here. On AMD driver
    // 32.0.21033.2001 + cohtml, wrapping the main-menu transition group
    // reliably hangs the render thread ~30s after the game enters the
    // main menu. Other working CS2 mods (e.g. krzychu124/Traffic) avoid
    // that extend target and only use moduleRegistry.append(...) — we do
    // the same.
    moduleRegistry.append("GameTopLeft", MainMenuButton);
    moduleRegistry.append("GameBottomRight", ChatIcon);
    moduleRegistry.append("GameBottomRight", CooperativeOverlay);

    // ChatPanel is exposed via the game's GamePanel system rather than
    // mounted directly in the React tree.
    const gamePanelComponents = getModule(
        "game-ui/game/components/game-panel-renderer.tsx",
        "gamePanelComponents",
    );
    gamePanelComponents["CS2M.UI.ChatPanel"] = ChatPanel;
};

export default register;