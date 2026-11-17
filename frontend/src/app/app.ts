import { DatePipe } from '@angular/common';
import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EMPTY, catchError, exhaustMap, filter, finalize, switchMap, timer } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
  private readonly destroyRef = inject(DestroyRef);

  protected readonly orders = signal<Order[]>([]);
  protected readonly inventory = signal<InventoryItem[]>([]);
  protected readonly activeView = signal<'orders' | 'inventory'>('orders');
  protected readonly isCheckingSession = signal(true);
  protected readonly isAuthenticated = signal(false);
  protected readonly isLoading = signal(true);
  protected readonly isInventoryLoading = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly isSavingInventory = signal(false);
  protected readonly isSigningIn = signal(false);
  protected readonly error = signal('');
  protected readonly authError = signal('');
  protected readonly inventoryError = signal('');
  protected readonly submitError = signal('');
  protected readonly successMessage = signal('');
  protected readonly inventorySuccessMessage = signal('');
  protected customerId = '';
  protected loginEmail = '';
  protected loginPassword = '';
  protected currentUserEmail = '';
  protected inventoryProductId = '';
  protected inventoryQuantity = 0;
  protected items: OrderItemDraft[] = [{ productId: '', quantity: 1 }];

  ngOnInit(): void {
    this.checkSession();
    timer(3000, 3000)
      .pipe(
        filter(() => this.isAuthenticated() && this.activeView() === 'orders'),
        exhaustMap(() => this.ordersApi.getOrders().pipe(
          catchError(() => {
            this.error.set('No se pudieron actualizar los pedidos. Comprueba la conexión.');
            return EMPTY;
          }),
        )),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((orders) => {
        this.orders.set(orders);
        this.error.set('');
      });
  }

  protected login(): void {
    this.isSigningIn.set(true);
    this.authError.set('');
    this.ordersApi.login(this.loginEmail.trim(), this.loginPassword)
      .pipe(
        switchMap(() => this.ordersApi.getCurrentUser()),
        finalize(() => this.isSigningIn.set(false)),
      )
      .subscribe({
        next: (user) => {
          this.currentUserEmail = user.email;
          this.isAuthenticated.set(true);
          this.loginPassword = '';
          this.refreshOrders();
        },
        error: () => this.authError.set('Correo o contraseña incorrectos.'),
      });
  }

  protected logout(): void {
    this.ordersApi.logout().subscribe({
      next: () => {
        this.isAuthenticated.set(false);
        this.currentUserEmail = '';
        this.orders.set([]);
        this.inventory.set([]);
        this.authError.set('');
      },
      error: () => this.authError.set('No se pudo cerrar la sesión. Inténtalo de nuevo.'),
    });
  }

  private checkSession(): void {
    this.ordersApi.getCurrentUser()
      .pipe(finalize(() => this.isCheckingSession.set(false)))
      .subscribe({
        next: (user) => {
          this.currentUserEmail = user.email;
          this.isAuthenticated.set(true);
          this.refreshOrders();
        },
        error: () => this.isAuthenticated.set(false),
      });
  }

  protected showView(view: 'orders' | 'inventory'): void {
    this.activeView.set(view);
    if (view === 'inventory') {
      this.refreshInventory();
    } else {
      this.refreshOrders();
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
