import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { UserAvatar } from './user-avatar';

describe('UserAvatar', () => {
  it('shows the Google photo when there is one', () => {
    render(<UserAvatar name='Maria Silva' pictureUrl='https://lh3.googleusercontent.com/a/maria' />);

    const img = screen.getByRole('img', { name: 'Maria Silva' });
    expect(img).toHaveAttribute('src', 'https://lh3.googleusercontent.com/a/maria');
    expect(screen.queryByText('MS')).not.toBeInTheDocument();
  });

  it('falls back to the initials when there is no photo', () => {
    render(<UserAvatar name='Maria Silva' pictureUrl={null} />);

    expect(screen.getByText('MS')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  // Google rotates the URL when the account changes its photo, so a stored one can 404 at any time.
  it('falls back to the initials when the photo fails to load', () => {
    render(<UserAvatar name='Maria Silva' pictureUrl='https://lh3.googleusercontent.com/a/expirada' />);

    fireEvent.error(screen.getByRole('img', { name: 'Maria Silva' }));

    expect(screen.getByText('MS')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  // Google blocks requests carrying a referrer it does not expect, which renders as a broken image.
  it('requests the photo without a referrer', () => {
    render(<UserAvatar name='Maria Silva' pictureUrl='https://lh3.googleusercontent.com/a/maria' />);

    expect(screen.getByRole('img', { name: 'Maria Silva' })).toHaveAttribute('referrerpolicy', 'no-referrer');
  });
});
