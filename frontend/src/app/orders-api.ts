import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface OrderItem {
  productId: string;
  quantity: number;
}

export interface Order {
  id: string;
  customerId: string;
  items: OrderItem[];
  status: 'Pending' | 'Confirmed' | 'Rejected';
  createdAtUtc: string;
}

export interface CreateOrderRequest {
  customerId: string;
  items: OrderItem[];
}

@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/orders';

  getOrders(): Observable<Order[]> {
    return this.http.get<Order[]>(this.endpoint);
  }

  createOrder(request: CreateOrderRequest): Observable<Order> {
    return this.http.post<Order>(this.endpoint, request);
  }
}