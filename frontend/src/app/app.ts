import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { InventoryItem, Order, OrdersApi } from './orders-api';

interface OrderItemDraft {
  productId: string;
  quantity: number;
}

@Component({
  selector: 'app-root',
  imports: [DatePipe, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly ordersApi = inject(OrdersApi);

  protected readonly orders = signal<Order[]>([]);
  protected readonly inventory = signal<InventoryItem[]>([]);
  protected readonly activeView = signal<'orders' | 'inventory'>('orders');
  protected readonly isLoading = signal(true);
  protected readonly isInventoryLoading = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly isSavingInventory = signal(false);
  protected readonly error = signal('');
  protected readonly inventoryError = signal('');
  protected readonly submitError = signal('');
  protected readonly successMessage = signal('');
  protected readonly inventorySuccessMessage = signal('');
  protected customerId = '';
  protected inventoryProductId = '';
  protected inventoryQuantity = 0;
  protected items: OrderItemDraft[] = [{ productId: '', quantity: 1 }];

  ngOnInit(): void {
    this.refreshOrders();
  }

  protected showView(view: 'orders' | 'inventory'): void {
    this.activeView.set(view);
    if (view === 'inventory') {
      this.refreshInventory();
    }
  }

  protected pendingCount(): number {
    return this.orders().filter((order) => order.status === 'Pending').length;
  }

  protected unitCount(): number {
    return this.orders().reduce((total, order) => total + this.orderUnits(order), 0);
  }

  protected orderUnits(order: Order): number {
    return order.items.reduce((total, item) => total + item.quantity, 0);
  }

  protected addItem(): void {
    this.items = [...this.items, { productId: '', quantity: 1 }];
  }

  protected removeItem(index: number): void {
    if (this.items.length > 1) {
      this.items = this.items.filter((_, itemIndex) => itemIndex !== index);
    }
  }

  protected refreshOrders(): void {
    this.isLoading.set(true);
    this.error.set('');
    this.ordersApi.getOrders()
      .pipe(finalize(() => this.isLoading.set(false)))
      .subscribe({
        next: (orders) => this.orders.set(orders),
        error: () => this.error.set('Comprueba que el servicio de pedidos esté iniciado e inténtalo de nuevo.'),
      });
  }

  protected refreshInventory(): void {
    this.isInventoryLoading.set(true);
    this.inventoryError.set('');
    this.ordersApi.getInventory()
      .pipe(finalize(() => this.isInventoryLoading.set(false)))
      .subscribe({
        next: (inventory) => this.inventory.set(inventory),
        error: () => this.inventoryError.set('No se pudo cargar el inventario. Comprueba la conexión e inténtalo de nuevo.'),
      });
  }

  protected editInventoryItem(item: InventoryItem): void {
    this.inventoryProductId = item.productId;
    this.inventoryQuantity = item.quantityAvailable;
    this.inventorySuccessMessage.set('');
  }

  protected saveInventory(): void {
    const productId = this.inventoryProductId.trim();
    if (!productId || !Number.isInteger(this.inventoryQuantity) || this.inventoryQuantity < 0) {
      this.inventoryError.set('Indica un SKU y una cantidad entera igual o mayor que cero.');
      return;
    }

    this.isSavingInventory.set(true);
    this.inventoryError.set('');
    this.inventorySuccessMessage.set('');
    this.ordersApi.setInventory(productId, this.inventoryQuantity)
      .pipe(finalize(() => this.isSavingInventory.set(false)))
      .subscribe({
        next: (updatedItem) => {
          this.inventory.update((items) => [
            ...items.filter((item) => item.productId !== updatedItem.productId),
            updatedItem,
          ].sort((first, second) => first.productId.localeCompare(second.productId)));
          this.inventoryProductId = '';
          this.inventoryQuantity = 0;
          this.inventorySuccessMessage.set(`Existencias de ${updatedItem.productId} actualizadas.`);
        },
        error: () => this.inventoryError.set('No se pudieron guardar las existencias. Revisa los datos e inténtalo de nuevo.'),
      });
  }

  protected submitOrder(): void {
    this.isSubmitting.set(true);
    this.submitError.set('');
    this.successMessage.set('');

    this.ordersApi.createOrder({
      customerId: this.customerId.trim(),
      items: this.items.map((item) => ({
        productId: item.productId.trim(),
        quantity: Number(item.quantity),
      })),
    }).pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (order) => {
          this.orders.update((orders) => [order, ...orders]);
          this.successMessage.set(`Pedido ${order.id.slice(0, 8).toUpperCase()} registrado.`);
          this.customerId = '';
          this.items = [{ productId: '', quantity: 1 }];
        },
        error: () => this.submitError.set('No se pudo registrar el pedido. Revisa los datos y vuelve a intentar.'),
      });
  }
}
