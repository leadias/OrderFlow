import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { Order, OrdersApi } from './orders-api';

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
  protected readonly isLoading = signal(true);
  protected readonly isSubmitting = signal(false);
  protected readonly error = signal('');
  protected readonly submitError = signal('');
  protected readonly successMessage = signal('');
  protected customerId = '';
  protected items: OrderItemDraft[] = [{ productId: '', quantity: 1 }];

  ngOnInit(): void {
    this.refreshOrders();
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
