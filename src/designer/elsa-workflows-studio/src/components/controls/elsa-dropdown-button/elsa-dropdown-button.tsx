import {Component, Event, EventEmitter, h, Prop} from '@stencil/core';
import {leave, toggle} from 'el-transition'
import {registerClickOutside} from "stencil-click-outside";
import {DropdownButtonItem, DropdownButtonOrigin} from "./models";

@Component({
    tag: 'elsa-dropdown-button',
    styleUrl: 'elsa-dropdown-button.css',
    shadow: false,
})
export class ElsaContextMenu {
    @Prop() text: string;
    @Prop() icon?: any;
    @Prop() origin: DropdownButtonOrigin = DropdownButtonOrigin.TopLeft;
    @Prop() items: Array<DropdownButtonItem> = [];

    @Event() itemSelected: EventEmitter<DropdownButtonItem>

    contextMenu: HTMLElement;

    closeContextMenu() {
        if (!!this.contextMenu)
            leave(this.contextMenu);
    }

    toggleMenu() {
        if (!!this.contextMenu)
            toggle(this.contextMenu);
    }

    getOriginClass(): string {
        switch (this.origin) {
            case DropdownButtonOrigin.TopLeft:
                return `left-0 origin-top-left`;
            case DropdownButtonOrigin.TopRight:
            default:
                return 'right-0 origin-top-right';
        }
    }

    async onItemClick(e: Event, menuItem: DropdownButtonItem) {
        e.preventDefault();
        this.itemSelected.emit(menuItem);
        this.closeContextMenu();
    }

    render() {
        return (
            <div class="relative" ref={el => registerClickOutside(this, el, this.closeContextMenu)}>
                <button onClick={e => this.toggleMenu()} type="button"
                        class="w-full bg-white border border-gray-300 rounded-md shadow-sm px-4 py-2 inline-flex justify-center text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
                        aria-haspopup="true" aria-expanded="false">
                    {this.renderIcon()}
                    {this.text}
                    <svg class="ml-2.5 -mr-1.5 h-5 w-5 text-gray-400" x-description="Heroicon name: chevron-down" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                        <path fill-rule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clip-rule="evenodd"/>
                    </svg>
                </button>
                {this.renderMenu()}
            </div>
        );
    }

    renderMenu() {
        if (this.items.length == 0)
            return;

        const originClass = this.getOriginClass();

        return <div ref={el => this.contextMenu = el} class={`hidden ${originClass} z-10 absolute mt-2 w-56 rounded-md shadow-lg bg-white ring-1 ring-black ring-opacity-5`}>
            <div class="py-1" role="menu" aria-orientation="vertical">
                {this.renderItems()}
            </div>
        </div>;
    }

    renderItems() {
        return this.items.map(item => {
            const selectedCssClass = item.isSelected ? "bg-blue-600 hover:bg-blue-700 text-white" : "hover:bg-gray-100 text-gray-700 hover:text-gray-900";

            return !!item.url
                ? <stencil-route-link onClick={e => this.closeContextMenu()} url={item.url} class={`block px-4 py-2 text-sm ${selectedCssClass}`} role="menuitem">{item.text}</stencil-route-link>
                : <a href="#" onClick={e => this.onItemClick(e, item)} class={`block px-4 py-2 text-sm ${selectedCssClass}`} role="menuitem">{item.text}</a>;
        })
    }

    renderIcon() {
        if (!this.icon)
            return;

        return this.icon;
    }
}
